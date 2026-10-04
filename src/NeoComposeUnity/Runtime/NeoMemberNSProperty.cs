// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
                if (member.returnTypeInfo is not null)
                    return member.returnTypeInfo;
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
        // Inlined into the generated accessor, so the result is copied out of
        // one frame fewer.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NSGetterResult Compute(string thisValueId) =>
            ReadThisRow(thisValueId) is { } row
                ? ComputeInternal(null, row)
                : MissingReceiver(thisValueId);

        private static NSGetterResult MissingReceiver(string thisValueId) =>
            NSGetterResult.Error($"thisValueId '{thisValueId}' not found in client values");

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
            if (ReadThisRow(thisValueId) is not { } row)
            {
                return SetterError(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return SetInternal(value, null, row);
        }

        // An accessor usually reads or sets one row's property again, so the
        // node of the row last read looks a repeat up once and unwraps it
        // through the node's memo.
        private MemberValue? ReadThisRow(string thisValueId)
        {
            if (thisNode is not null && thisNode.id != thisValueId)
                thisNode = null;
            return client.ReadValue(ownership, thisValueId, ref thisNode);
        }

        private NeoValueNode? thisNode;

        private NSGetterResult ComputeInternal(object? thisValue, MemberValue? thisRow)
        {
            var getter = resolvedGetter;
            if (getter is null)
            {
                return NSGetterResult.Error(
                    "Compiled `getter` not yet available — save the code to compile it.");
            }

            // A row-backed receiver shares the nested-dispatch memo: the key
            // is the one `this.X` would use, so a getter read every frame (the
            // equipped item, the clock) re-evaluates only after a row or grid
            // cell it read changes. Ad-hoc receivers are never memoized.
            bool memoize = thisValue is null && thisRow is not null && client.CanMemoizeGetters;
            bool holdsValuelessReads = false;
            if (memoize)
            {
                // The entry this node last read answers a repeat read of the
                // same row until the memo forgets it, without building and
                // hashing the memo's key.
                NeoClient.GetterMemoEntry? hit = memoEntry is { forgotten: false } kept && memoRowId == thisRow!.id
                    && client.HoldsCurrentReads(kept)
                    ? kept
                    : null;
                if (hit is null && (hit = client.FindMemoizedGetter(MemoKey(thisRow!), out holdsValuelessReads)) is not null)
                {
                    memoEntry = hit;
                    memoRowId = thisRow.id;
                }
                if (hit is not null)
                {
                    if (hit.list is not null)
                    {
                        var listCtx = client.CreateGetterContext(ownership);
                        if (NSGetterEvaluator.ResolveMemoizedList(hit.list, hit.listEntryMember, listCtx) is { } hitList)
                        {
                            client.ReplayGetterReads(hit);
                            return NSGetterResult.Ok(NSGetterEvaluator.ResolveHostCollection(hitList, listCtx));
                        }
                    }
                    else if (hit.row is null)
                    {
                        client.ReplayGetterReads(hit);
                        return NSGetterResult.Ok(hit.scalar);
                    }
                    else if (client.ReadReplayReference(hit.row.valueId, ref hit.row.node, hit.row.ownership) is { } hitRow)
                    {
                        client.ReplayGetterReads(hit);
                        var hitCtx = client.CreateGetterContext(ownership);
                        return NSGetterResult.Ok(NSGetterEvaluator.ResolveHostCollection(
                            NSGetterEvaluator.UnwrapMemoizedRow(hitRow, hitCtx, hit.row), hitCtx));
                    }
                    client.ForgetMemoizedGetter(MemoKey(thisRow!));
                }
            }

            // Build the Context first so we can unwrap row-based
            // bindings through its cache. Both `__root__` and
            // `__this__` need to participate in the cache so dispatch
            // on `root.Assets.X` and `this.foo` rounds-trips through
            // reference equality.
            var ctx = client.RentDirectFunctionContext(ownership);

            object? boundThis = thisValue;
            if (boundThis is null && thisRow is not null)
            {
                // Only Compute(thisValueId) passes a row: the one thisNode holds.
                boundThis = NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership, thisNode);
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
                        if (boundThis is not null)
                            break;
                    }
                    cursor = cursor.parent;
                }
            }

            // A live valueless entry already holds what this evaluation reads.
            if (holdsValuelessReads)
                memoize = false;
            NeoClient.GetterCaptureFrame enclosingCapture = memoize
                ? client.BeginGetterReadCapture()
                : default;
            NeoClient.GetterCaptureFrame capture = default;
            object? value = null;
            object? hostValue = null;
            string? error = null;
            try
            {
                ctx.BindThis(boundThis);
                // The rented context is this read's own, so the body binds
                // its handlers there rather than on a fork, which would also
                // keep the context out of the pool.
                value = NSGetterEvaluator.Evaluate(getter, ctx, System.Array.Empty<object?>(), handlerFrame: -1);
                // Inside the capture, so a watch hears a change to a stored
                // collection's entries as well as to the collection.
                hostValue = NSGetterEvaluator.ResolveHostCollection(value, ctx);
            }
            catch (NSGetterRuntimeError ex)
            {
                error = ex.Message;
            }
            catch (System.Exception ex)
            {
                error = $"Evaluator error: {ex.Message}";
            }
            finally
            {
                if (memoize)
                    capture = client.EndGetterReadCapture(enclosingCapture);
            }
            if (memoize)
            {
                NeoClient.GetterMemoKey memoKey = MemoKey(thisRow!);
                memoEntry = null;
                if (!client.CanMemoizeGetters)
                    client.RecycleGetterCapture(capture);
                // A failed read keeps only what it read, so a watch hears the
                // change that lets it succeed.
                else if (error is not null)
                    client.MemoizeGetterReads(memoKey, capture);
                else if (value is NeoScriptObject { attachedId: null } constructed)
                {
                    constructed.ShareGetterResult();
                    memoEntry = client.MemoizeGetter(memoKey, constructed, null, capture);
                }
                else if (value is null or string or bool or double or int or long or float)
                    memoEntry = client.MemoizeGetter(memoKey, value, null, capture);
                else if (NSGetterEvaluator.FindRowReference(value, ctx) is { } resultRef)
                {
                    client.ShareConstructedGetterRow(resultRef, value, ctx, capture);
                    memoEntry = client.MemoizeGetter(memoKey, null, resultRef, capture);
                }
                else if (value is object?[] entries
                    && NSGetterEvaluator.MemoizableList(entries, ctx, out Member? entryMember) is { } list)
                    memoEntry = client.MemoizeGetter(memoKey, null, null, capture, list, entryMember);
                else
                    client.MemoizeGetterReads(memoKey, capture);
                memoRowId = memoKey.rowId;
            }
            // Effects a replay inside this read queued run once it settles.
            client.DrainDeferredEffects();
            if (error is not null)
                return NSGetterResult.Error(error);
            client.ReturnDirectFunctionContext(ctx, hostValue);
            return NSGetterResult.Ok(hostValue);
        }

        private NeoClient.GetterMemoEntry? memoEntry;
        private string? memoRowId;
        private NeoClient.GetterMemoKey MemoKey(MemberValue thisRow) =>
            new(ownership, thisRow.id, member.id, ownership);

        private CallGetterPointer? setterSite;

        private NSSetterResult SetInternal(
            object? value,
            object? thisValue,
            MemberValue? thisRow)
        {
            var ctx = client.RentDirectFunctionContext(ownership);

            object? boundThis = ResolveThisValue(thisValue, thisRow, ctx);
            if (boundThis is null)
            {
                return SetterError("Cannot invoke setter on a null receiver.");
            }

            // A C# set is the write `this.X = value` makes, so it resolves
            // through a site of its own the way that write's does.
            string effectiveMemberId = NSGetterEvaluator.ResolveSetterMember(
                setterSite ??= new CallGetterPointer { memberId = member.id },
                boundThis,
                ctx,
                out Member? resolvedMember);
            var resolvedProperty = resolvedMember as NSPropertyMember;
            FunctionWithReturnType? setter = resolvedProperty?.setter;
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

            NSPropertyMember effectiveProperty = resolvedProperty ?? member;
            SetterTerminalLogger? terminalLogger = null;
            try
            {
                // The rented context is this call's own, so the setter enters it in place.
                ctx.PushSetter(effectiveMemberId, boundThis);
                var execution = NeoScriptExecutor.ExecuteSetter(
                    client,
                    setter,
                    normalizedValue,
                    ctx,
                    NeoScriptExecutionOptions.ForUnityProperty(client, effectiveMemberId));
                if (!execution.IsPaused)
                {
                    // A suspended setter's continuation keeps the context.
                    client.ReturnDirectFunctionContext(ctx, null);
                    return NSSetterResult.Ok();
                }

                terminalLogger = new SetterTerminalLogger(effectiveProperty);
                ObservePendingExecution(execution, terminalLogger);
                return NSSetterResult.Pending();
            }
            catch (System.Exception ex)
            {
                (terminalLogger ??= new SetterTerminalLogger(effectiveProperty)).Log(ex);
                return NSSetterResult.Error(ex.Message);
            }
        }

        private object? ResolveThisValue(
            object? thisValue,
            MemberValue? thisRow,
            NSGetterEvaluator.Context ctx)
        {
            if (thisValue is not null)
                return thisValue;
            if (thisRow is not null)
            {
                return NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership, thisNode);
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
                    if (resolved is not null)
                        return resolved;
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
                if (Interlocked.Exchange(ref logged, 1) != 0)
                    return;
                Debug.LogError(
                    $"NeoScript property setter '{property.name}' ({property.id}) failed: " +
                    exception.Message);
            }
        }
    }
}
