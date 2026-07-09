// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Attribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Terminal resolution of one generic param at a concrete type
    /// (mirrors the TS-side <c>TGenericEnvEntry</c> in
    /// <c>src/models/custom-types/generics.ts</c>):
    /// <list type="bullet">
    ///   <item><description><b>bound</b> — the param resolves to a concrete
    ///   attribute record (the type argument).</description></item>
    ///   <item><description><b>unbound</b> — still open at the resolved
    ///   type; <see cref="unboundParamId"/> is the deepest forward target
    ///   (the param a further descendant must bind).</description></item>
    /// </list>
    /// </summary>
    public sealed class NeoGenericEnvEntry
    {
        private NeoGenericEnvEntry(string? attributeId, string? unboundParamId)
        {
            this.attributeId = attributeId;
            this.unboundParamId = unboundParamId;
        }

        public static NeoGenericEnvEntry Bound(string attributeId) =>
            new(attributeId, null);

        public static NeoGenericEnvEntry Unbound(string paramId) =>
            new(null, paramId);

        public bool IsBound => attributeId is not null;

        /// <summary>Terminal binding attribute id; set iff <see cref="IsBound"/>.</summary>
        public string? attributeId { get; }

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
    /// (specs/custom-type-generics.md Decision 8) — the fields that change
    /// the projected C#/runtime type, and nothing else. Validation
    /// refinements (min/max, decimalPoints, templateId, column settings)
    /// are deliberately excluded, exactly as C# attributes like
    /// <c>[Range]</c> don't change a member's type. <see cref="required"/>
    /// IS included: nullability is part of the type (<c>float</c> vs
    /// <c>float?</c>). Mirrors the TS-side <c>TTypeArgumentSignature</c>.
    /// </summary>
    public sealed class NeoTypeArgumentSignature
    {
        public AttributeType type;
        public bool required;

        /// <summary>String only. Normalized: absent means localizable.</summary>
        public bool? localizable;

        /// <summary>Enum only.</summary>
        public string? enumId;

        /// <summary>Enum only.</summary>
        public bool? multiselect;

        /// <summary>Custom only.</summary>
        public string? customTypeId;

        /// <summary>Custom only — recursive argument signatures by target param id.</summary>
        public Dictionary<string, NeoTypeArgumentSignature>? args;

        /// <summary>List only — "ordered" or "unordered" (absent normalizes to ordered).</summary>
        public string? listKind;

        /// <summary>Dictionary only — "string" or "enum" (absent normalizes to string).</summary>
        public string? keyKind;

        /// <summary>Dictionary only — set iff <see cref="keyKind"/> is "enum".</summary>
        public string? keyEnumId;

        /// <summary>List / Dictionary only — recursive entry signature.</summary>
        public NeoTypeArgumentSignature? entry;
    }

    /// <summary>
    /// Generic-parameter resolution for the dotnet runtime
    /// (specs/custom-type-generics.md §9). Mirrors the web-side single
    /// source of truth, <c>src/models/custom-types/generics.ts</c> — keep
    /// both sides in sync:
    /// <list type="bullet">
    ///   <item><description>environment resolution walking the
    ///   <c>extendsTypeId</c> chain child-first and following forward
    ///   chains downward (<see cref="ResolveEnv(NeoClient, string)"/>);</description></item>
    ///   <item><description>the Decision-10 substitution field partition
    ///   (<see cref="SubstituteAttribute"/>);</description></item>
    ///   <item><description>the Decision-9 <c>genericBindings</c> value
    ///   stamp (<see cref="ComputeGenericBindingsStamp"/> /
    ///   <see cref="EnvFromStamp"/> / <see cref="StampGenericBindings"/>);</description></item>
    ///   <item><description>invariant type-argument signatures + admission
    ///   (<see cref="SignatureOfAttribute"/> /
    ///   <see cref="ConstructedSlotAccepts"/>).</description></item>
    /// </list>
    /// One deliberate deviation from the web module: the SDK operates on
    /// wire records directly rather than resolving <c>extendsAttributeId</c>
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
        /// <paramref name="typeId"/>'s inheritance chain to its terminal
        /// entry at <paramref name="typeId"/> — mirroring the web's
        /// <c>resolveGenericEnv</c>. Tolerant of incomplete data (mid-edit
        /// drafts): a missing binding resolves to unbound rather than
        /// throwing; write-path validation lives web-side.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveEnv(
            NeoClient client,
            string typeId)
        {
            var chain = CustomTypeInheritance.ResolveChain(
                typeId,
                id => client.TryGetType(id, out CustomType? match) ? match : null);
            return ResolveEnv(chain);
        }

        /// <summary>
        /// Chain-based overload for callers that already resolved the
        /// child-first inheritance chain (e.g.
        /// <see cref="NeoAttributeCustom"/>'s type context).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveEnv(
            IList<CustomType> chain)
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
            IList<CustomType> chain,
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
                if (binding.kind == NeoGenericBindingKinds.Attribute)
                {
                    return NeoGenericEnvEntry.Bound(binding.attributeId!);
                }
                cursor = binding.genericParamId!;
            }
            return NeoGenericEnvEntry.Unbound(cursor);
        }

        /// <summary>
        /// True when every generic param in scope of
        /// <paramref name="typeId"/>'s chain is bound — i.e., the type is
        /// instantiable generics-wise. Types with no generics anywhere in
        /// their chain are trivially closed.
        /// </summary>
        public static bool IsClosedType(NeoClient client, string typeId)
        {
            foreach (var entry in ResolveEnv(client, typeId).Values)
            {
                if (!entry.IsBound) return false;
            }
            return true;
        }

        /// <summary>
        /// Binding environment for descending into a Custom INSTANCE through
        /// its slot attribute (specs/custom-type-generics.md §4.1) — the
        /// mirror of the web's <c>resolveInstanceEnv</c>. The chain env of
        /// the row's effective type resolves params bound by named subtypes;
        /// the slot's constructed <c>customTypeArguments</c> overlay the
        /// params the chain leaves unbound — a constructed slot
        /// (<c>GenericTest&lt;Color&gt;</c>) closes the open type AT THE
        /// USAGE SITE without a named subtype, so its arguments are the
        /// terminal bindings for exactly those params. Chain bindings win
        /// where both exist (a named closed subtype picked into a
        /// constructed slot — admission guarantees signature equality, so
        /// they agree).
        ///
        /// Generic-kind arguments (still-open forwards) stay unbound:
        /// enclosing contexts substitute the slot attribute before descent,
        /// so a concrete instance never reaches here with a forward —
        /// reaching an unbound param during substitution remains the "open
        /// types are not instantiable" error.
        ///
        /// Identity with <see cref="ResolveEnv(IList{CustomType})"/> for
        /// slots without arguments, and the empty env for concrete types —
        /// concrete documents pay nothing.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveInstanceEnv(
            NeoClient client,
            string effectiveTypeId,
            IReadOnlyDictionary<string, GenericBinding>? customTypeArguments)
        {
            var chain = CustomTypeInheritance.ResolveChain(
                effectiveTypeId,
                id => client.TryGetType(id, out CustomType? match) ? match : null);
            return ResolveInstanceEnv(chain, customTypeArguments);
        }

        /// <summary>
        /// Chain-based overload of
        /// <see cref="ResolveInstanceEnv(NeoClient, string, IReadOnlyDictionary{string, GenericBinding})"/>
        /// for callers that already resolved the child-first inheritance
        /// chain (e.g. <see cref="NeoAttributeCustom"/>'s type context).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveInstanceEnv(
            IList<CustomType> chain,
            IReadOnlyDictionary<string, GenericBinding>? customTypeArguments)
        {
            var env = ResolveEnv(chain);
            if (customTypeArguments is null || customTypeArguments.Count == 0)
            {
                return env;
            }
            // Copy before overlaying — ResolveEnv may return the shared
            // EmptyEnv instance, which must never be mutated.
            var overlaid = new Dictionary<string, NeoGenericEnvEntry>(env.Count + customTypeArguments.Count);
            foreach (var pair in env)
            {
                overlaid[pair.Key] = pair.Value;
            }
            foreach (var pair in customTypeArguments)
            {
                if (pair.Value.IsForward) continue;
                if (overlaid.TryGetValue(pair.Key, out NeoGenericEnvEntry existing)
                    && existing.IsBound)
                {
                    continue;
                }
                overlaid[pair.Key] = NeoGenericEnvEntry.Bound(pair.Value.attributeId!);
            }
            return overlaid;
        }

        /// <summary>
        /// The first unbound param in <paramref name="env"/>, or <c>null</c>
        /// when every param is bound — the instantiability check for a
        /// Custom slot (closed named type OR fully constructed slot).
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
        /// <see cref="AttributeValue.genericBindings"/> stamp — the
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
        /// <paramref name="attribute"/>'s subtree: its own
        /// <c>genericParamId</c>, generic-kind constructed arguments, and
        /// recursion through collection entry attributes and attribute-kind
        /// arguments (which may themselves be partially generic, e.g.
        /// <c>C&lt;T&gt; : B&lt;List&lt;T&gt;&gt;</c>). Mirrors the web's
        /// <c>referencedGenericParams</c>.
        /// </summary>
        public static HashSet<string> ReferencedGenericParams(
            NeoClient client,
            Attribute attribute)
        {
            var result = new HashSet<string>();
            CollectReferencedGenericParams(client, attribute, new HashSet<string>(), result);
            return result;
        }

        private static void CollectReferencedGenericParams(
            NeoClient client,
            Attribute attribute,
            HashSet<string> seen,
            HashSet<string> result)
        {
            if (!string.IsNullOrEmpty(attribute.id) && !seen.Add(attribute.id))
            {
                return;
            }
            if (attribute is GenericAttribute generic)
            {
                result.Add(generic.genericParamId);
                return;
            }
            if (attribute is CustomAttribute custom)
            {
                if (custom.customTypeArguments is null) return;
                foreach (var arg in custom.customTypeArguments.Values)
                {
                    if (arg.IsForward)
                    {
                        result.Add(arg.genericParamId!);
                        continue;
                    }
                    if (!client.TryGetAttribute(arg.attributeId!, out Attribute? argAttr)) continue;
                    CollectReferencedGenericParams(client, argAttr, seen, result);
                }
                return;
            }
            string? entryAttributeId = attribute switch
            {
                ListAttribute list => list.entryAttributeId,
                DictionaryAttribute dictionary => dictionary.entryAttributeId,
                _ => null,
            };
            if (entryAttributeId is null) return;
            if (!client.TryGetAttribute(entryAttributeId, out Attribute? entryAttr)) return;
            CollectReferencedGenericParams(client, entryAttr, seen, result);
        }

        /// <summary>
        /// Computes the <c>genericBindings</c> stamp for a collection value
        /// row created from <paramref name="collectionAttribute"/> under
        /// <paramref name="env"/>: the terminal binding attribute id for
        /// exactly the params the entry subtree references. Returns
        /// <c>null</c> when the subtree references no params (no stamp).
        /// Throws when a referenced param is unresolvable — unreachable when
        /// web-side write validation holds, since open types cannot be
        /// instantiated.
        /// </summary>
        public static Dictionary<string, string>? ComputeGenericBindingsStamp(
            NeoClient client,
            Attribute collectionAttribute,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            var referenced = ReferencedGenericParams(client, collectionAttribute);
            if (referenced.Count == 0) return null;
            var stamp = new Dictionary<string, string>(referenced.Count);
            foreach (var paramId in referenced)
            {
                if (!env.TryGetValue(paramId, out NeoGenericEnvEntry entry))
                {
                    throw new System.InvalidOperationException(
                        $"ComputeGenericBindingsStamp: generic param '{paramId}' referenced by attribute '{collectionAttribute.id}' ({collectionAttribute.name}) is not present in the binding environment.");
                }
                if (!entry.IsBound)
                {
                    throw new System.InvalidOperationException(
                        $"ComputeGenericBindingsStamp: generic param '{paramId}' referenced by attribute '{collectionAttribute.id}' ({collectionAttribute.name}) is unbound in the binding environment — values of open generic types cannot be created.");
                }
                stamp[paramId] = entry.attributeId!;
            }
            return stamp;
        }

        /// <summary>
        /// Creation-side convenience: computes and assigns the stamp on a
        /// freshly-minted List/Dictionary <paramref name="row"/>. No-op for
        /// non-collection attributes, for entry subtrees that reference no
        /// params, and for rows that already carry a stamp (the stamp is
        /// immutable — a clone-on-write shadow keeps the original).
        /// </summary>
        public static void StampGenericBindings(
            NeoClient client,
            Attribute collectionAttribute,
            AttributeValue row,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (collectionAttribute is not ListAttribute
                && collectionAttribute is not DictionaryAttribute)
            {
                return;
            }
            if (row.genericBindings is not null) return;
            row.genericBindings = ComputeGenericBindingsStamp(client, collectionAttribute, env);
        }

        /// <summary>
        /// The binding environment a node's freshly-minted collection rows
        /// are stamped under, resolved from the wrapper tree: an enclosing
        /// Custom node supplies its type context's environment; an enclosing
        /// collection node supplies its own row's stamp; a parentless node
        /// has the empty environment. Mirrors the web's "creation always
        /// happens where the env is in hand" rule (spec Decision 9).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveContextEnv(
            NeoAttribute? contextParent)
        {
            if (contextParent is NeoAttributeCustom custom)
            {
                return custom.GenericEnv;
            }
            if (contextParent is NeoAttributeList list)
            {
                return EnvFromStamp(list.value?.genericBindings);
            }
            if (contextParent is NeoAttributeDictionary dictionary)
            {
                return EnvFromStamp(dictionary.value?.genericBindings);
            }
            return EmptyEnv;
        }

        // ------------------------------------------------------------------
        // Substitution (spec Decision 10).
        // ------------------------------------------------------------------

        /// <summary>
        /// Substitutes generic references in <paramref name="attribute"/>
        /// through <paramref name="env"/> — the mirror of the web's
        /// <c>substituteAttribute</c>:
        /// <list type="bullet">
        ///   <item><description><b>Generic slots</b> resolve to their
        ///   binding attribute. The binding supplies <c>type</c>, all
        ///   per-type config, <c>defaultValue</c>, and <c>required</c>
        ///   (nullability is part of the type — one <c>T</c> is one type).
        ///   The slot keeps its identity/placement fields: <c>id</c>,
        ///   <c>name</c>, <c>locked</c>, <c>storage</c>, <c>storageMap</c>.
        ///   Preserving <c>id</c> is what keeps parent-value records, child
        ///   node resolution, and NeoScript pointer IR working with zero
        ///   wire changes. <c>extendsAttributeId</c> is stripped.</description></item>
        ///   <item><description><b>Constructed Custom slots</b> substitute
        ///   generic-kind arguments to their terminal binding attributes;
        ///   unbound forwards are kept (open context — downstream consumers
        ///   substitute again once a concrete env exists).</description></item>
        ///   <item><description><b>Collections pass through unchanged</b> —
        ///   entry substitution is lazy at entry sites via the value row's
        ///   <c>genericBindings</c> stamp.</description></item>
        ///   <item><description>Everything else is identity.</description></item>
        /// </list>
        /// </summary>
        public static Attribute SubstituteAttribute(
            NeoClient client,
            Attribute attribute,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (attribute is GenericAttribute generic)
            {
                Attribute binding = ResolveTerminalBinding(
                    client,
                    generic.genericParamId,
                    generic.name,
                    env);
                Attribute substituted = binding.ShallowClone();
                substituted.id = generic.id;
                substituted.name = generic.name;
                substituted.locked = generic.locked;
                // Placement fields are slot-owned. A null declaration means
                // "inherit from placement parent" and must not fall back to
                // the binding's own declaration (bindings are type
                // arguments, not placements).
                substituted.storage = generic.storage;
                substituted.storageMap = generic.storageMap;
                substituted.extendsAttributeId = null;
                return substituted;
            }
            if (attribute is CustomAttribute custom)
            {
                var args = custom.customTypeArguments;
                if (args is null) return attribute;
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
                            $"SubstituteAttribute: constructed argument for param '{pair.Key}' on attribute '{custom.name}' ({custom.id}) forwards generic param '{arg.genericParamId}', which is not present in the binding environment.");
                    }
                    if (!entry.IsBound)
                    {
                        // Open context (e.g. resolving against the open type
                        // itself): keep the forward — downstream consumers
                        // substitute again once a concrete env exists.
                        substitutedArgs[pair.Key] = arg;
                        continue;
                    }
                    substitutedArgs[pair.Key] = new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = entry.attributeId,
                    };
                    changed = true;
                }
                if (!changed) return attribute;
                var substitutedCustom = (CustomAttribute)custom.ShallowClone();
                substitutedCustom.customTypeArguments = substitutedArgs;
                return substitutedCustom;
            }
            return attribute;
        }

        /// <summary>
        /// Resolves a param to its terminal binding attribute through
        /// <paramref name="env"/>, with a distinct error per failure mode.
        /// </summary>
        private static Attribute ResolveTerminalBinding(
            NeoClient client,
            string genericParamId,
            string slotName,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (!env.TryGetValue(genericParamId, out NeoGenericEnvEntry entry))
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is not present in the binding environment (is the attribute referencing a param outside its placement scope?).");
            }
            if (!entry.IsBound)
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is unbound in the binding environment — only closed types are instantiable.");
            }
            if (!client.TryGetAttribute(entry.attributeId!, out Attribute? bindingAttr))
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': binding attribute '{entry.attributeId}' for param '{genericParamId}' does not exist in the document.");
            }
            return bindingAttr;
        }

        // ------------------------------------------------------------------
        // Type-argument signatures (spec Decision 8).
        // ------------------------------------------------------------------

        /// <summary>
        /// Computes the projection signature of an attribute under
        /// <paramref name="env"/>. Generic references resolve through the
        /// environment first, so a partially-generic binding
        /// (<c>C&lt;T&gt; : B&lt;List&lt;T&gt;&gt;</c> seen from
        /// <c>D : C&lt;Float&gt;</c>) signatures as its fully-concrete form.
        /// Mirrors the web's <c>typeArgumentSignatureOfAttribute</c>.
        /// </summary>
        public static NeoTypeArgumentSignature SignatureOfAttribute(
            NeoClient client,
            Attribute attribute,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>();
            if (!string.IsNullOrEmpty(attribute.id) && !seen.Add(attribute.id))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfAttribute revisited attribute '{attribute.id}' — constructed arguments form a reference cycle.");
            }
            if (attribute is GenericAttribute generic)
            {
                Attribute binding = ResolveTerminalBinding(
                    client,
                    generic.genericParamId,
                    generic.name,
                    env);
                return SignatureOfAttribute(client, binding, env, seen);
            }
            bool required = attribute.required;
            if (attribute is EnumAttribute enumAttr)
            {
                return new NeoTypeArgumentSignature
                {
                    type = AttributeType.Enum,
                    required = required,
                    enumId = enumAttr.enumId,
                    multiselect = enumAttr.multiselect,
                };
            }
            if (attribute is CustomAttribute customAttr)
            {
                var args = new Dictionary<string, NeoTypeArgumentSignature>();
                if (customAttr.customTypeArguments is not null)
                {
                    foreach (var pair in customAttr.customTypeArguments)
                    {
                        args[pair.Key] = SignatureOfBinding(client, pair.Value, env, seen);
                    }
                }
                return new NeoTypeArgumentSignature
                {
                    type = AttributeType.Custom,
                    required = required,
                    customTypeId = customAttr.customTypeId,
                    args = args,
                };
            }
            if (attribute is ListAttribute listAttr)
            {
                return new NeoTypeArgumentSignature
                {
                    type = AttributeType.List,
                    required = required,
                    listKind = listAttr.listKind ?? NeoListKinds.Ordered,
                    entry = EntrySignature(client, listAttr.entryAttributeId, env, seen),
                };
            }
            if (attribute is DictionaryAttribute dictionaryAttr)
            {
                return new NeoTypeArgumentSignature
                {
                    type = AttributeType.Dictionary,
                    required = required,
                    keyKind = dictionaryAttr.keyKind ?? NeoDictionaryKeyKinds.String,
                    keyEnumId = dictionaryAttr.keyEnumId,
                    entry = EntrySignature(client, dictionaryAttr.entryAttributeId, env, seen),
                };
            }
            if (attribute is StringAttribute stringAttr)
            {
                return new NeoTypeArgumentSignature
                {
                    type = AttributeType.String,
                    required = required,
                    // The field initializer defaults to true, so an absent
                    // wire value and an explicit `true` signature identically
                    // — the same normalization as the web module.
                    localizable = stringAttr.localizable,
                };
            }
            return new NeoTypeArgumentSignature
            {
                type = attribute.type,
                required = required,
            };
        }

        private static NeoTypeArgumentSignature EntrySignature(
            NeoClient client,
            string entryAttributeId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string> seen)
        {
            if (!client.TryGetAttribute(entryAttributeId, out Attribute? entryAttr))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfAttribute: entry attribute '{entryAttributeId}' does not exist in the document.");
            }
            return SignatureOfAttribute(client, entryAttr, env, seen);
        }

        /// <summary>
        /// Signature of a binding/argument: forwards resolve through
        /// <paramref name="env"/> first; attribute bindings signature
        /// directly. Mirrors the web's <c>typeArgumentSignatureOfBinding</c>.
        /// </summary>
        public static NeoTypeArgumentSignature SignatureOfBinding(
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
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = entry.attributeId,
                    },
                    env,
                    seen);
            }
            if (!client.TryGetAttribute(binding.attributeId!, out Attribute? attr))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfBinding: binding attribute '{binding.attributeId}' does not exist in the document.");
            }
            return SignatureOfAttribute(client, attr, env, seen);
        }

        /// <summary>
        /// Deep structural equality over projection signatures — invariant
        /// (spec Decision 8: <c>Foo&lt;Child&gt;</c> is not a
        /// <c>Foo&lt;Parent&gt;</c>). Mirrors the web's
        /// <c>signaturesEqual</c>.
        /// </summary>
        public static bool SignaturesEqual(
            NeoTypeArgumentSignature a,
            NeoTypeArgumentSignature b)
        {
            if (a.type != b.type) return false;
            if (a.required != b.required) return false;
            if (a.type == AttributeType.String)
            {
                return a.localizable == b.localizable;
            }
            if (a.type == AttributeType.Enum)
            {
                return a.enumId == b.enumId && a.multiselect == b.multiselect;
            }
            if (a.type == AttributeType.Custom)
            {
                if (a.customTypeId != b.customTypeId) return false;
                var aArgs = a.args ?? new Dictionary<string, NeoTypeArgumentSignature>();
                var bArgs = b.args ?? new Dictionary<string, NeoTypeArgumentSignature>();
                if (aArgs.Count != bArgs.Count) return false;
                foreach (var pair in aArgs)
                {
                    if (!bArgs.TryGetValue(pair.Key, out NeoTypeArgumentSignature bArg)) return false;
                    if (!SignaturesEqual(pair.Value, bArg)) return false;
                }
                return true;
            }
            if (a.type == AttributeType.List)
            {
                if (a.listKind != b.listKind) return false;
                return SignaturesEqual(a.entry!, b.entry!);
            }
            if (a.type == AttributeType.Dictionary)
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
        /// Decides whether a value of <paramref name="valueTypeId"/> is
        /// admissible in the slot declared by
        /// <paramref name="slotAttribute"/> under
        /// <paramref name="contextEnv"/> (the enclosing closed context's
        /// environment — used to substitute forwarded arguments). Mirrors
        /// the web's <c>constructedSlotAccepts</c>:
        /// <list type="number">
        ///   <item><description><paramref name="valueTypeId"/> must be the
        ///   slot's <c>customTypeId</c> or a descendant (nominal walk);</description></item>
        ///   <item><description><paramref name="valueTypeId"/> must be
        ///   closed (open types are never instantiable);</description></item>
        ///   <item><description>for each constructed argument, the value
        ///   type's terminal binding must signature-match the
        ///   context-substituted argument — invariantly.</description></item>
        /// </list>
        /// <c>isAbstract</c> is deliberately not checked here — value
        /// creation paths already reject abstract types, and this function
        /// also tests mid-hierarchy compatibility. Generated writable
        /// setters that accept a caller-chosen subtype id (and any future
        /// SDK-side value-admission path) should call this before minting a
        /// row for a constructed slot.
        /// </summary>
        public static NeoSlotAdmission ConstructedSlotAccepts(
            NeoClient client,
            CustomAttribute slotAttribute,
            string valueTypeId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> contextEnv)
        {
            var chain = CustomTypeInheritance.ResolveChain(
                valueTypeId,
                id => client.TryGetType(id, out CustomType? match) ? match : null);
            if (chain.Count == 0)
            {
                return NeoSlotAdmission.Reject(
                    $"type '{valueTypeId}' does not exist in the document");
            }
            bool isDescendant = false;
            foreach (var t in chain)
            {
                if (t.id != slotAttribute.customTypeId) continue;
                isDescendant = true;
                break;
            }
            if (!isDescendant)
            {
                return NeoSlotAdmission.Reject(
                    $"type '{chain[0].name}' is not '{slotAttribute.customTypeId}' or one of its descendants");
            }
            // The pick's env is its chain env overlaid with the SLOT's
            // constructed arguments: for a constructed slot, the declared
            // type itself (or an open descendant that forwards the params)
            // is closed BY the slot — rejecting it as "open" would leave
            // constructed slots with no valid pick at all (spec §3.4).
            var valueEnv = ResolveInstanceEnv(chain, slotAttribute.customTypeArguments);
            foreach (var entry in valueEnv.Values)
            {
                if (entry.IsBound) continue;
                return NeoSlotAdmission.Reject(
                    $"type '{chain[0].name}' is open (generic param '{entry.unboundParamId}' is unbound) and cannot be instantiated");
            }
            var args = slotAttribute.customTypeArguments;
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
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = valueBinding.attributeId,
                    },
                    valueEnv);
                if (!SignaturesEqual(slotSignature, valueSignature))
                {
                    return NeoSlotAdmission.Reject(
                        $"type-argument signature mismatch for param '{pair.Key}' (arguments compare invariantly)");
                }
            }
            return NeoSlotAdmission.Ok;
        }
    }
}
