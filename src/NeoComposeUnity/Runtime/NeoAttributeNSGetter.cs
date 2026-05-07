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
        public NSGetterResult Compute(object? thisValue = null) =>
            ComputeInternal(thisValue, /*thisRow*/ null);

        /// <summary>
        /// Convenience overload that takes a value-id string and
        /// looks up the corresponding row internally. Unlike the
        /// <see cref="Compute(object?)"/> overload, this routes the
        /// <c>__this__</c> binding through the evaluator's
        /// per-context cache + reverse index — so <c>is</c>-checks
        /// against Custom types and runtime-override dispatch on
        /// <c>this</c> itself work correctly. Prefer this overload
        /// when the receiver is a known stored row; the object-only
        /// overload is for ad-hoc / synthesized records.
        /// </summary>
        public NSGetterResult Compute(string thisValueId)
        {
            if (!client.TryGetValue(thisValueId, out AttributeValue? row))
            {
                return NSGetterResult.Error(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return ComputeInternal(null, row);
        }

        private NSGetterResult ComputeInternal(object? thisValue, AttributeValue? thisRow)
        {
            var getter = resolvedGetter;
            if (getter is null)
            {
                return NSGetterResult.Error(
                    "Compiled `getter` not yet available — save the code to compile it.");
            }

            // Build the Context first so we can unwrap row-based
            // bindings through its cache. Both `__root__` and
            // `__this__` need to participate in the cache so dispatch
            // on `root.Assets.X` and `this.foo` rounds-trips through
            // reference equality.
            var ctx = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            object? rootValue = ResolveRootValue(ctx);
            ctx = ctx.WithRoot(rootValue);

            object? boundThis = thisValue;
            if (boundThis is null && thisRow is not null)
            {
                boundThis = NSGetterEvaluator.UnwrapRow(thisRow, ctx);
            }
            if (boundThis is null)
            {
                // Walk parent chain for a row to unwrap through the cache.
                NeoAttribute? cursor = parent;
                for (int i = 0; cursor is not null && i < 32; i++)
                {
                    if (cursor.value is ObjectAttributeValue obj)
                    {
                        boundThis = NSGetterEvaluator.UnwrapRow(obj, ctx);
                        if (boundThis is not null) break;
                    }
                    cursor = cursor.parent;
                }
            }

            try
            {
                var value = NSGetterEvaluator.Evaluate(getter, ctx.WithThis(boundThis));
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
        /// Synthesizes the runtime <c>__root__</c> value:
        /// <c>{ Assets: &lt;assets-record&gt;, Save: &lt;save-record&gt; }</c>.
        /// The two roots come from <see cref="NeoClient.assets"/> /
        /// <see cref="NeoClient.save"/>'s underlying value records;
        /// either entry is null when the corresponding root attribute
        /// has no stored value. Both records are unwrapped through
        /// the evaluator's cache so chains like <c>root.Assets.X</c>
        /// participate in reference-equality dispatch.
        /// </summary>
        private object? ResolveRootValue(NSGetterEvaluator.Context ctx)
        {
            var root = new Dictionary<string, object?>(2);
            root["Assets"] = client.assets.value is ObjectAttributeValue a
                ? NSGetterEvaluator.UnwrapRow(a, ctx)
                : null;
            root["Save"] = client.save.value is ObjectAttributeValue s
                ? NSGetterEvaluator.UnwrapRow(s, ctx)
                : null;
            return root;
        }
    }
}
