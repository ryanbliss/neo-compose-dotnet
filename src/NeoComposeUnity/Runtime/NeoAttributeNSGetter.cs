// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an NSGetter-typed attribute. The stored value is
    /// always null — the runtime computes the value at evaluation
    /// time by walking the IR exposed via
    /// <see cref="NSGetterAttribute.getter"/>.
    ///
    /// <para>No Saved variant — NSGetter values are derived, not set.
    /// <see cref="Compute"/> walks the IR via
    /// <see cref="NSGetterEvaluator"/>; <see cref="resolvedGetter"/>
    /// and <see cref="resolvedReturnTypeInfo"/> handle the
    /// <c>extendsAttributeId</c> chain so override-form NSGetter
    /// rows that omit their own <c>getter</c> / <c>returnTypeInfo</c>
    /// fall through to the parent's compiled IR.</para>
    /// </summary>
    public class NeoAttributeNSGetter
        : NeoAttribute<NSGetterAttribute, NullAttributeValue>
    {
        public NeoAttributeNSGetter(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeNSGetter(NeoClient client, NSGetterAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        /// <summary>
        /// The compiled getter for this attribute, walking
        /// <c>extendsAttributeId</c> when this row is an override that
        /// inherits its IR from a parent. Returns null when no
        /// ancestor has a compiled getter.
        /// </summary>
        public FunctionWithReturnType? resolvedGetter
        {
            get
            {
                if (attribute.getter is not null) return attribute.getter;
                return CustomTypeInheritance.WalkExtendsAttributeChain(
                    attribute.id,
                    id => client.TryGetAttribute(id, out Attribute? a) ? a : null,
                    a => a is NSGetterAttribute ng ? ng.getter : null,
                    requireType: AttributeType.NSGetter);
            }
        }

        /// <summary>
        /// The declared return type, walking the override chain when
        /// this row is an override that inherits its return type from
        /// a parent. Returns null if no ancestor declares one.
        /// </summary>
        public TypeInfo? resolvedReturnTypeInfo
        {
            get
            {
                if (attribute.returnTypeInfo is not null) return attribute.returnTypeInfo;
                return CustomTypeInheritance.WalkExtendsAttributeChain(
                    attribute.id,
                    id => client.TryGetAttribute(id, out Attribute? a) ? a : null,
                    a => a is NSGetterAttribute ng ? ng.returnTypeInfo : null,
                    requireType: AttributeType.NSGetter);
            }
        }

        /// <summary>
        /// Walks the compiled IR (<see cref="resolvedGetter"/>) and
        /// returns the produced value wrapped in an
        /// <see cref="NSGetterResult"/>. Catches
        /// <see cref="NSGetterRuntimeError"/> and any other unexpected
        /// exception so callers always have something to render —
        /// matches the TS-side <c>NSGetterValueNodeVM.result</c>
        /// pattern.
        ///
        /// <para><paramref name="thisValue"/> binds the synthetic
        /// <c>__this__</c> parameter. When omitted (the default), it's
        /// resolved by walking <see cref="NeoAttribute.parent"/> for
        /// the nearest Custom-shaped ancestor — matches the TS
        /// <c>resolveThisFromParentChain</c> behavior. Pass an
        /// explicit value to override (e.g., for tests or for
        /// project-root NSGetters with no Custom parent).</para>
        /// </summary>
        public NSGetterResult Compute(object? thisValue = null)
        {
            var getter = resolvedGetter;
            if (getter is null)
            {
                return NSGetterResult.Error(
                    "Compiled `getter` not yet available — save the code to compile it.");
            }
            object? boundThis = thisValue ?? ResolveThisFromParentChain();
            object? rootValue = ResolveRootValue();

            try
            {
                var ctx = new NSGetterEvaluator.Context(client, boundThis, rootValue);
                var value = NSGetterEvaluator.Evaluate(getter, ctx);
                return NSGetterResult.Ok(value);
            }
            catch (NSGetterRuntimeError ex)
            {
                return NSGetterResult.Error(ex.Message);
            }
            catch (System.Exception ex)
            {
                return NSGetterResult.Error($"Evaluator error: {ex.Message}");
            }
        }

        /// <summary>
        /// Walks <see cref="NeoAttribute.parent"/> looking for the
        /// nearest ancestor whose value resolves to a Custom record
        /// (an <see cref="ObjectAttributeValue"/> with non-null
        /// content). That record's content map becomes
        /// <c>__this__</c>. Returns null if no Custom ancestor is
        /// found — caller binds <c>__this__</c> to null and any
        /// <c>this.foo</c> reference in the user's NeoScript surfaces
        /// as a runtime error.
        ///
        /// <para>32-hop cap defends against accidental cycles in the
        /// parent chain (none should exist by construction, but the
        /// SDK is defensive).</para>
        /// </summary>
        private object? ResolveThisFromParentChain()
        {
            NeoAttribute? cursor = parent;
            for (int i = 0; cursor is not null && i < 32; i++)
            {
                if (cursor.value is ObjectAttributeValue obj && obj.value is not null)
                {
                    // Wrap as Dictionary<string, object?> for the
                    // evaluator (which wants object? values for
                    // schema-key lookups).
                    var record = new Dictionary<string, object?>(obj.value.Count);
                    foreach (var kvp in obj.value) record[kvp.Key] = kvp.Value;
                    return record;
                }
                cursor = cursor.parent;
            }
            return null;
        }

        /// <summary>
        /// Synthesizes the runtime <c>__root__</c> value:
        /// <c>{ assets: &lt;assets-record&gt;, save: &lt;save-record&gt; }</c>.
        /// The two roots come from <see cref="NeoClient.assets"/> /
        /// <see cref="NeoClient.save"/>'s underlying value records;
        /// either entry is null when the corresponding root attribute
        /// has no stored value.
        /// </summary>
        private object? ResolveRootValue()
        {
            var root = new Dictionary<string, object?>(2);
            root["assets"] = ExtractRecord(client.assets.value);
            root["save"] = ExtractRecord(client.save.value);
            return root;
        }

        private static IDictionary<string, object?>? ExtractRecord(ObjectAttributeValue? row)
        {
            if (row?.value is null) return null;
            var record = new Dictionary<string, object?>(row.value.Count);
            foreach (var kvp in row.value) record[kvp.Key] = kvp.Value;
            return record;
        }
    }
}
