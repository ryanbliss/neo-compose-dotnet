// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an NSProperty-typed member. The stored value is
    /// always null — the runtime computes the value at evaluation
    /// time by walking the IR exposed via
    /// <see cref="NSPropertyMember.getter"/>.
    ///
    /// <para>There is no stored-value Writable variant. Getters are derived,
    /// while <see cref="Set(object?, object?)"/> executes the optional
    /// compiled setter and lets that NeoScript mutate its own targets.
    /// <see cref="Compute"/> walks the IR via
    /// <see cref="NSGetterEvaluator"/>; <see cref="resolvedGetter"/> exposes
    /// the centralized effective compiled body, while
    /// <see cref="resolvedReturnTypeInfo"/> walks inherited signature
    /// metadata.</para>
    /// </summary>
    public class NeoMemberNSProperty
        : NeoMember<NSPropertyMember, NullMemberValue>
    {
        public NeoMemberNSProperty(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberNSProperty(NeoClient client, NSPropertyMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// The effective compiled getter for this member. Sparse inheritance
        /// and authored-code null clears are projected during client load.
        /// </summary>
        public FunctionWithReturnType? resolvedGetter
        {
            get => member.getter;
        }

        /// <summary>
        /// The effective compiled setter for this property.
        /// </summary>
        public FunctionWithReturnType? resolvedSetter
        {
            get => member.setter;
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
                if (member.returnTypeInfo is not null) return member.returnTypeInfo;
                return NeoSchemaClassInheritance.WalkExtendsMemberChain(
                    member.id,
                    id => client.TryGetMember(id, out Member? a) ? a : null,
                    a => a is NSPropertyMember ng ? ng.returnTypeInfo : null,
                    requireKind: MemberKind.NSProperty);
            }
        }

        /// <summary>
        /// Walks the compiled IR (<see cref="resolvedGetter"/>) and
        /// returns the produced value wrapped in an
        /// <see cref="NSGetterResult"/>. Catches
        /// <see cref="NSGetterRuntimeError"/> and any other unexpected
        /// exception so callers always have something to render —
        /// matches the TS-side <c>NSPropertyValueNodeVM.result</c>
        /// pattern.
        ///
        /// <para><paramref name="thisValue"/> binds the synthetic
        /// <c>__this__</c> parameter. When omitted (the default), it's
        /// resolved by walking <see cref="NeoMember.parent"/> for
        /// the nearest Class-shaped ancestor — matches the TS
        /// <c>resolveThisFromParentChain</c> behavior. Pass an
        /// explicit value to override (e.g., for tests or for
        /// project-root NSProperties with no Class parent).</para>
        /// </summary>
        public NSGetterResult Compute(object? thisValue = null) =>
            ComputeInternal(thisValue, /*thisRow*/ null);

        /// <summary>
        /// Convenience overload that takes a value-id string and
        /// looks up the corresponding row internally. Unlike the
        /// <see cref="Compute(object?)"/> overload, this routes the
        /// <c>__this__</c> binding through the evaluator's
        /// per-context cache + reverse index — so <c>is</c>-checks
        /// against Classes and runtime-override dispatch on
        /// <c>this</c> itself work correctly. Prefer this overload
        /// when the receiver is a known stored row; the object-only
        /// overload is for ad-hoc / synthesized records.
        /// </summary>
        public NSGetterResult Compute(string thisValueId)
        {
            if (!client.TryGetValue(ownership, thisValueId, out MemberValue? row))
            {
                return NSGetterResult.Error(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return ComputeInternal(null, row);
        }

        /// <summary>
        /// Executes this property's compiled setter. Deferred native
        /// Functions may make the accepted invocation pending; any eventual
        /// terminal error is logged by the SDK because the original property
        /// assignment has already returned.
        /// </summary>
        public NSSetterResult Set(object? value, object? thisValue = null) =>
            SetInternal(value, thisValue, /*thisRow*/ null);

        /// <summary>
        /// Executes the setter with <c>__this__</c> bound to a stored row id.
        /// Prefer this overload from generated property accessors so runtime
        /// override dispatch and Class <c>is</c> checks retain row identity.
        /// </summary>
        public NSSetterResult Set(string thisValueId, object? value)
        {
            if (!client.TryGetValue(ownership, thisValueId, out MemberValue? row))
            {
                return SetterError(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return SetInternal(value, null, row);
        }

        private NSGetterResult ComputeInternal(object? thisValue, MemberValue? thisRow)
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
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            object? rootValue = ResolveRootValue(ctx);
            ctx = ctx.WithRoot(rootValue);

            object? boundThis = thisValue;
            if (boundThis is null && thisRow is not null)
            {
                boundThis = NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership);
            }
            if (boundThis is null)
            {
                // Walk parent chain for a row to unwrap through the cache.
                NeoMember? cursor = parent;
                for (int i = 0; cursor is not null && i < 32; i++)
                {
                    if (cursor.value is ObjectMemberValue obj)
                    {
                        boundThis = NSGetterEvaluator.UnwrapRow(obj, ctx, cursor.ownership);
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

        private NSSetterResult SetInternal(
            object? value,
            object? thisValue,
            MemberValue? thisRow)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            object? rootValue = ResolveRootValue(ctx);
            ctx = ctx.WithRoot(rootValue);

            object? boundThis = ResolveThisValue(thisValue, thisRow, ctx);
            if (boundThis is null)
            {
                return SetterError("Cannot invoke setter on a null receiver.");
            }

            string effectiveMemberId = NeoScriptExecutor.ResolveEffectiveSetterMemberId(
                client,
                member.id,
                boundThis,
                ctx);
            var setter = NeoScriptExecutor.ResolveCompiledSetter(
                effectiveMemberId,
                client);
            if (setter is null)
            {
                return SetterError(
                    "Compiled `setter` not yet available — add and save setter code to compile it.");
            }

            TypeInfo? returnTypeInfo = resolvedReturnTypeInfo;
            if (returnTypeInfo is null)
            {
                return SetterError(
                    "Setter return type is unavailable — save the property to compile it.");
            }

            object? normalizedValue;
            try
            {
                normalizedValue = NormalizeSetterValue(
                    value,
                    returnTypeInfo,
                    ctx);
            }
            catch (System.Exception ex)
            {
                return SetterError($"Setter value conversion failed: {ex.Message}");
            }

            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = boundThis,
                ["__root__"] = rootValue,
                ["__value__"] = normalizedValue,
            };
            NSPropertyMember effectiveProperty = client.TryGetMember(
                effectiveMemberId, out NSPropertyMember? resolvedProperty)
                    ? resolvedProperty!
                    : member;
            var terminalLogger = new SetterTerminalLogger(effectiveProperty);
            try
            {
                var execution = NeoScriptExecutor.Execute(
                    client,
                    setter,
                    scope,
                    ctx.WithSetterPushed(effectiveMemberId).WithThis(boundThis),
                    NeoScriptExecutionOptions
                        .ForUnity(client)
                        .ForProperty(effectiveMemberId),
                    terminal => NeoScriptExecutor.ValidateStatementTerminal(
                        terminal,
                        "NeoScript property setter"));
                if (!execution.IsPaused) return NSSetterResult.Ok();

                ObservePendingExecution(execution, terminalLogger);
                return NSSetterResult.Pending();
            }
            catch (System.Exception ex)
            {
                terminalLogger.Log(ex);
                return NSSetterResult.Error(ex.Message);
            }
        }

        private object? ResolveThisValue(
            object? thisValue,
            MemberValue? thisRow,
            NSGetterEvaluator.Context ctx)
        {
            if (thisValue is not null) return thisValue;
            if (thisRow is not null)
            {
                return NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership);
            }
            NeoMember? cursor = parent;
            for (int i = 0; cursor is not null && i < 32; i++)
            {
                if (cursor.value is ObjectMemberValue obj)
                {
                    object? resolved = NSGetterEvaluator.UnwrapRow(
                        obj,
                        ctx,
                        cursor.ownership);
                    if (resolved is not null) return resolved;
                }
                cursor = cursor.parent;
            }
            return null;
        }

        private object? NormalizeSetterValue(
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx)
        {
            return NeoScriptValueMarshaller.Normalize(
                client,
                ownership,
                value,
                typeInfo,
                ctx,
                "setter value");
        }

        private NSSetterResult SetterError(string message)
        {
            Debug.LogError(
                $"NeoScript property setter '{member.name}' ({member.id}) failed: {message}");
            return NSSetterResult.Error(message);
        }

        private static void ObservePendingExecution(
            NeoScriptExecutionResult execution,
            SetterTerminalLogger terminalLogger)
        {
            execution.WhenDeferredSettled(
                resumed =>
                {
                    if (resumed.IsPaused)
                    {
                        ObservePendingExecution(resumed, terminalLogger);
                    }
                },
                terminalLogger.Log);
        }

        private sealed class SetterTerminalLogger
        {
            private readonly NSPropertyMember property;
            private int logged;

            internal SetterTerminalLogger(NSPropertyMember property)
            {
                this.property = property;
            }

            internal void Log(System.Exception exception)
            {
                if (Interlocked.Exchange(ref logged, 1) != 0) return;
                Debug.LogError(
                    $"NeoScript property setter '{property.name}' ({property.id}) failed: " +
                    exception.Message);
            }
        }

        /// <summary>
        /// Synthesizes the runtime <c>__root__</c> value:
        /// <c>{ Assets: &lt;assets-record&gt;, Save: &lt;save-record&gt; }</c>.
        /// The two roots come from <see cref="NeoClient.assets"/> /
        /// <see cref="NeoClient.save"/>'s underlying value records;
        /// either entry is null when the corresponding root member
        /// has no stored value. Both records are unwrapped through
        /// the evaluator's cache so chains like <c>root.Assets.X</c>
        /// participate in reference-equality dispatch.
        /// </summary>
        private object? ResolveRootValue(NSGetterEvaluator.Context ctx)
        {
            return NeoScriptValueMarshaller.ResolveRoot(client, ctx);
        }
    }
}
