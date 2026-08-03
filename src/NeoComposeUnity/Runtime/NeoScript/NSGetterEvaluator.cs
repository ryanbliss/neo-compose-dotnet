// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// Tracks Session-backed class values created by NeoScript constructor
    /// intrinsics for one logical invocation. Nested NSFunction/setter/getter
    /// executions share the tracker, so a temporary returned into its caller
    /// is not collected before the caller can attach or return it.
    /// </summary>
    internal sealed class NeoScriptAllocationTracker
    {
        private readonly NeoScriptExecutionBudgetLimits limits;
        private readonly HashSet<string> allocatedRootIds = new();
        private readonly HashSet<string> escapedRootIds = new();
        private int activeExecutions;
        private int loopIterations;
        private int workUnits;
        private int collectionVisits;
        private int producedCollectionEntries;
        private int constructedSessionRows;
        private int producedStringCharacters;

        internal NeoScriptAllocationTracker(
            NeoScriptExecutionBudgetLimits? limits = null)
        {
            this.limits = limits ?? new NeoScriptExecutionBudgetLimits();
        }

        internal void EnterExecution()
        {
            if (activeExecutions == 0)
            {
                loopIterations = 0;
                workUnits = 0;
                collectionVisits = 0;
                producedCollectionEntries = 0;
                constructedSessionRows = 0;
                producedStringCharacters = 0;
            }
            activeExecutions++;
        }

        /// <summary>
        /// Consumes one iteration from the P50 budget shared by the complete
        /// nested NeoScript invocation. The allocation tracker already has
        /// the required lifetime: child contexts share it, deferred execution
        /// keeps it active, and it resets only after the outermost frame exits.
        /// </summary>
        internal void ConsumeLoopIteration()
        {
            loopIterations++;
            if (loopIterations > NeoScriptExecutor.MaxLoopIterations)
            {
                throw new NeoScriptResourceLimitError(
                    "NeoScript loop iteration limit of 10000 exceeded.");
            }
            ConsumeWorkUnit();
        }

        internal void ConsumeWorkUnit(int amount = 1) =>
            Consume(
                ref workUnits,
                amount,
                limits.WorkUnits,
                "work unit");

        internal void ConsumeCollectionVisit(int amount = 1) =>
            Consume(
                ref collectionVisits,
                amount,
                limits.CollectionVisits,
                "collection visit");

        internal void ConsumeProducedCollectionEntry(int amount = 1) =>
            Consume(
                ref producedCollectionEntries,
                amount,
                limits.ProducedCollectionEntries,
                "produced collection entry");

        internal void ConsumeConstructedSessionRow(int amount = 1) =>
            Consume(
                ref constructedSessionRows,
                amount,
                limits.ConstructedSessionRows,
                "constructed Session row");

        internal void ConsumeProducedStringCharacters(int amount) =>
            Consume(
                ref producedStringCharacters,
                amount,
                limits.ProducedStringCharacters,
                "produced string character");

        internal void ConsumeCreatedSessionRows(
            IReadOnlyCollection<MemberValue> rows)
        {
            ConsumeConstructedSessionRow(rows.Count);
            int entryCount = 0;
            foreach (MemberValue row in rows)
            {
                if (row is ArrayMemberValue arrayRow)
                {
                    entryCount += arrayRow.value?.Length ?? 0;
                }
                else if (row is ObjectMemberValue objectRow)
                {
                    entryCount += objectRow.value?.Count ?? 0;
                }
            }
            ConsumeProducedCollectionEntry(entryCount);
        }

        private static void Consume(
            ref int consumed,
            int amount,
            int limit,
            string label)
        {
            if (amount < 0 || amount > limit - consumed)
            {
                throw new NeoScriptResourceLimitError(
                    $"NeoScript {label} limit of {limit} exceeded.");
            }
            consumed += amount;
        }

        internal void RegisterSessionRoot(string valueId)
        {
            if (!string.IsNullOrEmpty(valueId)) allocatedRootIds.Add(valueId);
        }

        /// <summary>
        /// A value crossing a function-call boundary is no longer owned by
        /// the current NeoScript frame. Mark its complete constructed graph as
        /// escaped; a later parent assignment may still move/import it.
        /// </summary>
        internal void MarkEscaped(object? value, NSGetterEvaluator.Context ctx)
        {
            MarkEscaped(value, ctx, new HashSet<object>());
        }

        private void MarkEscaped(
            object? value,
            NSGetterEvaluator.Context ctx,
            HashSet<object> visited)
        {
            if (value is null || value is string || value.GetType().IsValueType)
            {
                return;
            }
            if (!visited.Add(value)) return;

            string? valueId = NSGetterEvaluator.FindRowIdByReference(value, ctx);
            if (valueId is not null)
            {
                MarkAllocationGroupEscaped(valueId, ctx);
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    MarkEscaped(entry.Key, ctx, visited);
                    MarkEscaped(entry.Value, ctx, visited);
                }
                return;
            }
            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (object? entry in enumerable)
                {
                    MarkEscaped(entry, ctx, visited);
                }
            }
        }

        private void MarkAllocationGroupEscaped(
            string valueId,
            NSGetterEvaluator.Context ctx)
        {
            // A NeoScript return may expose any object-shaped row in a
            // constructor graph (for example a nested Class, List, or
            // Dictionary), rather than the constructor's root object itself.
            // Follow authoritative owned-parent edges back to every staged
            // constructor root so the complete allocation group survives the
            // terminal cleanup.
            var visited = new HashSet<string>();
            string cursor = valueId;
            while (visited.Add(cursor))
            {
                if (allocatedRootIds.Contains(cursor))
                {
                    escapedRootIds.Add(cursor);
                }
                if (!ctx.client.TryFindOwnedParent(
                        NeoValueOwnership.Session,
                        cursor,
                        out string? parentValueId)
                    || string.IsNullOrEmpty(parentValueId)
                    || parentValueId.StartsWith("member:", StringComparison.Ordinal)
                    || parentValueId.StartsWith("static:", StringComparison.Ordinal))
                {
                    break;
                }
                cursor = parentValueId;
            }
        }

        internal void ExitExecution(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionResult? terminalResult)
        {
            if (activeExecutions <= 0)
            {
                throw new InvalidOperationException(
                    "NeoScript allocation tracker execution depth underflow.");
            }
            activeExecutions--;
            if (activeExecutions != 0) return;

            if (terminalResult?.Returned == true)
            {
                MarkEscaped(terminalResult.ReturnValue, ctx);
            }

            foreach (string valueId in allocatedRootIds.ToArray())
            {
                if (escapedRootIds.Contains(valueId)) continue;
                // Unwrapping a Class row exposes schema values as stable-id
                // strings, so MarkEscaped cannot discover a nested constructed
                // Class by recursively walking the CLR return object. Preserve
                // it when its authoritative owned-parent chain reaches an
                // escaped constructed root instead. This is also the exact
                // tree-ownership signal used by assignment/import code.
                if (IsOwnedByEscapedRoot(client, valueId)) continue;
                // A constructor may be attached beneath a parentless Session
                // aggregate supplied by the host or retained from an earlier
                // NeoScript invocation. Such a parent is intentionally not a
                // global Session reachability root, but its authoritative
                // owned edge still transfers the constructor out of this
                // invocation's temporary allocation group. Follow only the
                // client's schema-aware owned-parent relation here: Lookup and
                // other reference payloads must never keep a constructor alive.
                if (IsOwnedByExternalSessionRoot(client, valueId)) continue;
                // External/global owners were ruled out above. Force-reclaim
                // the invocation-minted owned graph instead of ordinary
                // reachability GC: storage-key rows in unloaded partitions
                // are conservatively protected by normal GC, but these fresh
                // rows cannot belong to an unloaded authored graph.
                IReadOnlyCollection<string> removed =
                    client.RemoveTemporaryWritableValueGraph(
                        NeoValueOwnership.Session,
                        valueId);
                if (removed.Count == 0) continue;
                client.DisposeWrappersTouchingRows(removed);
                NSGetterEvaluator.EvictCachedRows(
                    ctx,
                    NeoValueOwnership.Session,
                    removed);
            }
            allocatedRootIds.Clear();
            escapedRootIds.Clear();
        }

        private bool IsOwnedByEscapedRoot(
            NeoClient client,
            string valueId)
        {
            var visited = new HashSet<string> { valueId };
            string cursor = valueId;
            while (client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                cursor,
                out string? parentValueId))
            {
                if (escapedRootIds.Contains(parentValueId)) return true;
                if (!visited.Add(parentValueId)) return false;
                cursor = parentValueId;
            }
            return false;
        }

        private bool IsOwnedByExternalSessionRoot(
            NeoClient client,
            string valueId)
        {
            var visited = new HashSet<string> { valueId };
            string cursor = valueId;
            while (client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                cursor,
                out string? parentValueId))
            {
                if (parentValueId.StartsWith("member:", StringComparison.Ordinal)
                    || parentValueId.StartsWith("static:", StringComparison.Ordinal))
                {
                    return true;
                }
                if (!visited.Add(parentValueId))
                {
                    // Malformed owned cycles are not an escape route. The
                    // ordinary defensive cleanup traversal will terminate on
                    // its own visited set.
                    return false;
                }
                cursor = parentValueId;
            }

            // A terminal current-invocation constructor root is still a
            // temporary. Any other terminal row belongs to a graph that
            // predates this allocation tracker and therefore owns the value.
            return cursor != valueId && !allocatedRootIds.Contains(cursor);
        }
    }

    /// <summary>
    /// Pure, stateless walker that evaluates a compiled
    /// <see cref="FunctionWithReturnType"/> NSProperty. C# port of the TS
    /// <c>evaluateNSGetter</c> in
    /// <c>src/view-models/neoscript-evaluator/evaluateNSGetter.ts</c> —
    /// feature-by-feature parity for instructions, all 14 pointer
    /// kinds, both operations, all 6 collection functions, schema-key
    /// dispatch with override walking, runtime <c>is</c> checks, and
    /// stringification.
    ///
    /// <para>Entry point:
    /// <see cref="Evaluate(FunctionWithReturnType, Context)"/>. Throws
    /// <see cref="NSGetterRuntimeError"/> on missing values, missing
    /// schema keys, out-of-bounds indices, type mismatches, force-unwrap
    /// of null, or a thrown statement. Wrapped by
    /// <see cref="NeoMemberNSProperty.Compute"/>'s try/catch.</para>
    /// </summary>
    public static class NSGetterEvaluator
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            object, Dictionary<string, int>> ListIdentityIndexes = new();

        /// <summary>
        /// Per-evaluation context: the project, the bound
        /// <c>__this__</c> / <c>__root__</c> values, and a cycle-detection
        /// stack of NSProperty member ids currently being evaluated.
        /// </summary>
        public class Context
        {
            public delegate object? FunctionCallHandler(
                CallFunctionPointer pointer,
                Dictionary<string, object?> scope,
                Context ctx);

            public NeoClient client { get; }
            public object? thisValue { get; }
            public object? rootValue { get; }
            public object? contextValue { get; }
            public INeoDialogueMemoryStore? memoryStore { get; }
            /// <summary>
            /// Stack of NSProperty member ids currently in-flight. Threaded
            /// through callGetter recursion via fresh-copy children so a
            /// cycle (`A.x` calls `B.y` calls `A.x` on a different receiver)
            /// trips before the runtime stack overflows.
            /// </summary>
            public IReadOnlyCollection<string> getterCallStack { get; }
            /// <summary>
            /// Stack of NSProperty member ids whose setters are currently
            /// executing. Kept separate from <see cref="getterCallStack"/>,
            /// but preserved by every child context so setter→getter→setter
            /// recursion is detected by the shared NeoScript executor.
            /// </summary>
            public IReadOnlyCollection<string> setterCallStack { get; }
            /// <summary>
            /// Ordered stack of NSFunction member ids currently executing.
            /// Unlike getter/setter cycle sets, recursion is valid and is only
            /// rejected once the runtime depth cap is reached.
            /// </summary>
            public IReadOnlyList<string> functionCallStack { get; }
            /// <summary>
            /// Ordered bound-delegate targets currently executing. This is a
            /// shared mutable stack so nested evaluator contexts retain cycle
            /// detection across closure and member-target boundaries.
            /// </summary>
            internal List<string> delegateCallStack { get; }
            /// <summary>
            /// P43 §7.2.3 — ordered names of the classes currently under
            /// construction. Deliberately separate from
            /// <see cref="functionCallStack"/>: a constructor chain recurses
            /// through member initializers and nested <c>new</c> expressions
            /// rather than through NSFunction calls, so the NSFunction cap
            /// never sees it. Bounded by
            /// <see cref="NeoGeneratedTypesSupport.MaxConstructionDepth"/>.
            /// </summary>
            public IReadOnlyList<string> constructionStack { get; }
            internal NeoValueOwnership valueOwnership { get; }

            /// <summary>
            /// Caches <c>valueId → unwrapped</c> CLR shape for every
            /// row touched during evaluation. Critical for two
            /// behaviors that the TS evaluator gets for free (because
            /// JS objects round-trip by reference):
            ///
            /// <list type="bullet">
            ///   <item><description><c>resolveValueIfId</c> returning the
            ///   *same* heap object for the same id, so chains like
            ///   <c>this.foo.bar</c> read through one receiver
            ///   instance.</description></item>
            ///   <item><description>Reference-equality lookups in
            ///   <see cref="FindRowClassIdByReference"/> /
            ///   <see cref="FindRowIdByReference"/> matching the
            ///   receiver back to its source row — needed for
            ///   <c>is</c>-checks against Classes and for
            ///   stringification of Class / List / Dictionary results.
            ///   </description></item>
            /// </list>
            ///
            /// Shared across the parent Context and every child built
            /// via <see cref="WithGetterPushed"/> / <see cref="WithThis"/>
            /// so a callGetter's inner evaluation sees the same row
            /// identities the outer evaluation built up.
            /// </summary>
            internal Dictionary<string, object?> rowUnwrapCache { get; }

            /// <summary>
            /// Reverse index of <see cref="rowUnwrapCache"/>: maps an
            /// unwrapped object back to the <c>valueId</c> that
            /// produced it. Built lazily as rows are unwrapped — only
            /// populated for object-shaped values (records, arrays)
            /// where reference equality is meaningful. Primitives
            /// (string / number / bool) skip the index because
            /// reference equality on boxed primitives would
            /// false-positive.
            /// </summary>
            internal Dictionary<object, RowReference> rowReverseIndex { get; }
            internal Dictionary<string, HashSet<string>> rowCacheKeysByRow { get; }
            internal FunctionCallHandler? functionCallHandler { get; }
            internal Dictionary<string, SchemaPlacement?> schemaPlacementCache { get; }
            internal Dictionary<string, string?> callableDispatchCache { get; }
            internal Dictionary<
                string,
                IReadOnlyDictionary<string, NeoGenericEnvEntry>>
                genericEnvironmentCache { get; }
            internal NeoScriptAllocationTracker allocationTracker { get; private set; }

            public Context(
                NeoClient client,
                object? thisValue,
                object? rootValue,
                object? contextValue = null,
                INeoDialogueMemoryStore? memoryStore = null,
                IReadOnlyCollection<string>? getterCallStack = null,
                Dictionary<string, object?>? rowUnwrapCache = null,
                Dictionary<object, RowReference>? rowReverseIndex = null,
                NeoValueOwnership valueOwnership = NeoValueOwnership.Save,
                FunctionCallHandler? functionCallHandler = null,
                IReadOnlyCollection<string>? setterCallStack = null,
                IReadOnlyList<string>? functionCallStack = null,
                Dictionary<string, SchemaPlacement?>? schemaPlacementCache = null,
                Dictionary<string, string?>? callableDispatchCache = null,
                Dictionary<string, HashSet<string>>? rowCacheKeysByRow = null,
                Dictionary<
                    string,
                    IReadOnlyDictionary<string, NeoGenericEnvEntry>>?
                    genericEnvironmentCache = null,
                IReadOnlyList<string>? constructionStack = null,
                List<string>? delegateCallStack = null,
                NeoScriptExecutionBudgetLimits? executionBudgetLimits = null)
            {
                this.client = client;
                this.thisValue = thisValue;
                this.rootValue = rootValue;
                this.contextValue = contextValue;
                this.memoryStore = memoryStore;
                this.getterCallStack = getterCallStack ?? System.Array.Empty<string>();
                this.setterCallStack = setterCallStack ?? System.Array.Empty<string>();
                this.rowUnwrapCache = rowUnwrapCache ?? new Dictionary<string, object?>();
                this.rowReverseIndex = rowReverseIndex
                    ?? new Dictionary<object, RowReference>(ReferenceEqualityComparer.Instance);
                this.rowCacheKeysByRow = rowCacheKeysByRow
                    ?? new Dictionary<string, HashSet<string>>();
                this.valueOwnership = valueOwnership;
                this.functionCallHandler = functionCallHandler;
                this.functionCallStack = functionCallStack ?? System.Array.Empty<string>();
                this.schemaPlacementCache = schemaPlacementCache
                    ?? new Dictionary<string, SchemaPlacement?>();
                this.callableDispatchCache = callableDispatchCache
                    ?? new Dictionary<string, string?>();
                this.genericEnvironmentCache = genericEnvironmentCache
                    ?? new Dictionary<
                        string,
                        IReadOnlyDictionary<string, NeoGenericEnvEntry>>();
                this.constructionStack = constructionStack
                    ?? System.Array.Empty<string>();
                this.delegateCallStack = delegateCallStack ?? new List<string>();
                allocationTracker = new NeoScriptAllocationTracker(
                    executionBudgetLimits);
            }

            private Context ShareAllocationTracker(Context child)
            {
                child.allocationTracker = allocationTracker;
                return child;
            }

            internal Context WithGetterPushed(string memberId)
            {
                var next = new HashSet<string>(getterCallStack) { memberId };
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    next,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithThis(object? newThisValue)
            {
                return ShareAllocationTracker(new Context(
                    client,
                    newThisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithRoot(object? newRootValue)
            {
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    newRootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithContext(object? newContextValue)
            {
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    newContextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithMemoryStore(INeoDialogueMemoryStore? newMemoryStore)
            {
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    newMemoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithFunctionCallHandler(
                FunctionCallHandler handler)
            {
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    handler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithSetterPushed(string memberId)
            {
                var next = new HashSet<string>(setterCallStack) { memberId };
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    next,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            internal Context WithFunctionPushed(string memberId)
            {
                var next = new List<string>(functionCallStack.Count + 1);
                next.AddRange(functionCallStack);
                next.Add(memberId);
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    next,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    constructionStack,
                    delegateCallStack));
            }

            /// <summary>
            /// P43 §7.2.3 — pushes <paramref name="className"/> onto the
            /// construction chain. Every nested member initializer, base
            /// constructor, and constructor body runs on the returned context,
            /// so a cyclic construction trips the depth cap with the chain that
            /// caused it rather than overflowing the runtime stack.
            /// </summary>
            internal Context WithConstructionPushed(string className)
            {
                var next = new List<string>(constructionStack.Count + 1);
                next.AddRange(constructionStack);
                next.Add(className);
                return ShareAllocationTracker(new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    functionCallHandler,
                    setterCallStack,
                    functionCallStack,
                    schemaPlacementCache,
                    callableDispatchCache,
                    rowCacheKeysByRow,
                    genericEnvironmentCache,
                    next,
                    delegateCallStack));
            }
        }

        /// <summary>
        /// Reference-only equality comparer for the
        /// <see cref="Context.rowReverseIndex"/>. .NET 5+ has this in
        /// the BCL; we polyfill for netstandard2.1.
        /// </summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        public readonly struct RowReference
        {
            public string valueId { get; }
            public NeoValueOwnership ownership { get; }
            public string? classId { get; }

            public RowReference(
                string valueId,
                NeoValueOwnership ownership,
                string? classId = null)
            {
                this.valueId = valueId;
                this.ownership = ownership;
                this.classId = classId;
            }
        }

        // ---------------------------------------------------------------
        // Entry
        // ---------------------------------------------------------------

        /// <summary>
        /// Walks <paramref name="getter"/> and returns the produced
        /// value. Throws <see cref="NSGetterRuntimeError"/> if the
        /// function falls off the end without an explicit return.
        /// </summary>
        public static object? Evaluate(FunctionWithReturnType getter, Context ctx) =>
            Evaluate(getter, ctx, Array.Empty<object?>());

        /// <summary>
        /// Evaluates a getter-shaped initializer with its optional class-header
        /// constructor parameters bound after <c>__this__</c>/<c>__root__</c>.
        /// P61 keeps these parameterized bodies only in declaration graphs;
        /// the declared-constructor path supplies the values when it creates a
        /// concrete instance.
        /// </summary>
        internal static object? Evaluate(
            FunctionWithReturnType getter,
            Context ctx,
            IReadOnlyList<object?> argumentValues)
        {
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = ctx.thisValue,
                ["__root__"] = ctx.rootValue,
                ["__context__"] = ctx.contextValue,
            };
            Variable[] parameters = getter.parameters ?? Array.Empty<Variable>();
            if (argumentValues.Count > 0
                && parameters.Length != argumentValues.Count + 2)
            {
                throw new NSGetterRuntimeError(
                    $"Initializer declares {Math.Max(0, parameters.Length - 2)} constructor parameter(s), but received {argumentValues.Count} value(s).");
            }
            for (int i = 0; i < argumentValues.Count; i++)
            {
                scope[parameters[i + 2].id] = argumentValues[i];
            }
            // Getters, actions, setters, and NSFunctions now share the same
            // effect-capable executor. Writability is a compile/runtime target
            // property, not a reason to maintain a second pure interpreter.
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                ctx.client,
                getter,
                scope,
                ctx);
            if (result.IsPaused)
            {
                throw new NSGetterRuntimeError(
                    $"Getter suspended on deferred Function '{result.SuspendedMemberId}'. Deferred calls are not supported by synchronous property evaluation.");
            }
            if (result.Returned)
            {
                // `return intExpr;` on a Decimal-typed getter class-checks via
                // exact int widening; the runtime number becomes a canonical
                // decimal string here (mirrors the TS evaluator's return seam).
                return result.ReturnValue;
            }
            throw new NSGetterRuntimeError("Function ended without a return statement");
        }

        /// <summary>
        /// Materialises the unwrapped CLR shape for an
        /// <see cref="MemberValue"/> row through the per-context
        /// cache + reverse index. Public so external callers (notably
        /// <see cref="NeoMemberNSProperty.Compute"/>) can pre-warm the
        /// cache when binding <c>__this__</c> to a known row — without
        /// going through the cache, <c>is</c>-checks against Class
        /// classes and runtime-override dispatch on the receiver wouldn't
        /// fire because reference equality would never round-trip.
        /// </summary>
        public static object? UnwrapRow(MemberValue row, Context ctx) =>
            UnwrapCached(row, ctx, ctx.valueOwnership);

        public static object? UnwrapRow(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership) =>
            UnwrapCached(row, ctx, ownership);

        internal static object? EvaluatePointer(
            Pointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            return EvalPointer(pointer, scope, ctx);
        }

        // ---------------------------------------------------------------
        // Pointers — 14 kinds
        // ---------------------------------------------------------------

        private static object? EvalPointer(
            Pointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            switch (pointer)
            {
                case ValuePointer vp:
                {
                    if (NeoDelegateValueConverter.LooksLikeValue(vp.value.value))
                    {
                        NeoDelegateValue closure = vp.value.value!
                            .ToObject<NeoDelegateValue>()!;
                        return closure.Capture(ctx.thisValue, ctx.rootValue);
                    }
                    return UnwrapJToken(vp.value.value);
                }
                case VariablePointer vrp:
                {
                    if (!scope.TryGetValue(vrp.variableId, out var v))
                    {
                        throw new NSGetterRuntimeError(
                            $"Variable '{vrp.variableId}' is not in scope");
                    }
                    return v;
                }
                case ReferencePointer rp:
                {
                    var ownership = ResolveOwnershipForValueId(ctx, rp.valueId);
                    if (!ctx.client.TryGetValue(ownership, rp.valueId, out MemberValue? row))
                    {
                        throw new NSGetterRuntimeError(
                            $"Missing value reference: {rp.valueId}");
                    }
                    return UnwrapCached(row, ctx, ownership);
                }
                case StaticMemberPointer staticPointer:
                {
                    if (!ctx.client.TryGetMember(
                            staticPointer.memberId,
                            out JsonMember? staticMember)
                        || !staticMember.isStatic)
                    {
                        throw new NSGetterRuntimeError(
                            $"Static member '{staticPointer.memberId}' was not found.");
                    }
                    NeoValueOwnership ownership =
                        ctx.client.ResolveStaticOwnership(staticMember);
                    if (!ctx.client.TryResolveStaticBinding(
                            staticMember.id,
                            out _,
                            out _,
                            out string? staticValueId))
                    {
                        return null;
                    }
                    if (!ctx.client.TryGetOverlaidValue(
                            ownership,
                            staticValueId,
                            out MemberValue? staticRow))
                    {
                        throw new NSGetterRuntimeError(
                            $"Static member '{staticMember.name}' is bound to missing value '{staticValueId}'.");
                    }
                    return UnwrapCached(
                        staticRow,
                        ctx,
                        ownership,
                        staticMember);
                }
                case KeyOfPointer kop:
                    return EvalKeyOf(
                        kop.keyOf,
                        scope,
                        ctx,
                        kop.optional == true,
                        kop.memberId);
                case OperationPointer op:
                    return EvalOperation(op.operation, scope, ctx);
                case FunctionPointer fp:
                    return EvalFunction(fp.function, scope, ctx);
                case ListLiteralPointer llp:
                {
                    ctx.allocationTracker.ConsumeProducedCollectionEntry(
                        llp.entries.Length);
                    var arr = new object?[llp.entries.Length];
                    for (int i = 0; i < llp.entries.Length; i++)
                    {
                        arr[i] = EvalPointer(llp.entries[i], scope, ctx);
                    }
                    return arr;
                }
                case DictLiteralPointer dlp:
                {
                    ctx.allocationTracker.ConsumeProducedCollectionEntry(
                        dlp.entries.Length);
                    var dict = new Dictionary<string, object?>();
                    foreach (var entry in dlp.entries)
                    {
                        var k = EvalPointer(entry.key, scope, ctx);
                        dict[k?.ToString() ?? "null"] = EvalPointer(entry.value, scope, ctx);
                    }
                    return dict;
                }
                case ForceUnwrapPointer fup:
                {
                    var v = EvalPointer(fup.pointer, scope, ctx);
                    if (v is null)
                    {
                        throw new NSGetterRuntimeError(
                            $"Unexpectedly found null while force-unwrapping a value (unwrapped pointer kind: {DescribePointer(fup.pointer)})");
                    }
                    return v;
                }
                case IsCheckPointer icp:
                {
                    var v = EvalPointer(icp.pointer, scope, ctx);
                    return RuntimeTypeCheck(v, icp.checkType, ctx);
                }
                case CallGetterPointer cgp:
                {
                    if (cgp.receiver.IsStatic)
                    {
                        ValidateStaticCallableReceiver(
                            cgp.receiver,
                            cgp.memberId,
                            "getter",
                            ctx);
                        return DispatchNSGetterById(
                            cgp.memberId,
                            receiver: null,
                            ctx);
                    }
                    var innerThis = EvalCallReceiver(cgp.receiver, scope, ctx);
                    if (cgp.optional == true && innerThis is null) return null;
                    // Try runtime dispatch via the receiver's classId merged
                    // schema first — same trick the TS evaluator uses to
                    // honor runtime overrides regardless of the static
                    // compile-time binding.
                    SchemaPlacement? placement = FindSchemaPlacementCached(
                        cgp.memberId, ctx);
                    if (placement is not null)
                    {
                        var dispatched = DispatchSchemaMember(innerThis, placement.schemaKey, ctx);
                        if (dispatched.kind == DispatchKind.Ok) return dispatched.value;
                    }
                    return DispatchNSGetterById(cgp.memberId, innerThis, ctx);
                }
                case CoalescePointer cp:
                {
                    var left = EvalPointer(cp.left, scope, ctx);
                    if (left is not null) return left;
                    return EvalPointer(cp.right, scope, ctx);
                }
                case ToBoolPointer tbp:
                {
                    var v = EvalPointer(tbp.pointer, scope, ctx);
                    return JsTruthy(v);
                }
                case StringifyPointer sp:
                {
                    var v = EvalPointer(sp.pointer, scope, ctx);
                    string result = FormatForInterp(v, sp.sourceType, ctx);
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
                case CallFunctionPointer functionCall:
                    return EvalFunctionCall(functionCall, scope, ctx);
                case CallDelegatePointer delegateCall:
                {
                    object? callable = EvalPointer(delegateCall.@delegate, scope, ctx);
                    if (callable is null)
                    {
                        if (delegateCall.optional == true) return null;
                        throw new NSGetterRuntimeError(
                            "Cannot invoke a null NeoDelegate value.");
                    }
                    var args = new object?[delegateCall.args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        args[i] = EvalPointer(delegateCall.args[i], scope, ctx);
                    }
                    return InvokeDelegate(callable, args, ctx);
                }
                case FunctionErrorCheckPointer functionErrorCheck:
                    return EvalFunctionErrorCheck(functionErrorCheck, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown pointer kind {pointer.GetType().Name}");
            }
        }

        /// <summary>
        /// Compact description of a pointer for error messages: enough to
        /// locate the failing expression (getter/member ids) from a log line
        /// without re-running the script.
        /// </summary>
        private static string DescribePointer(Pointer pointer)
        {
            switch (pointer)
            {
                case CallGetterPointer cgp:
                    return $"callGetter {cgp.memberId}";
                case KeyOfPointer kop:
                    return $"keyOf {EvalPointerKeyLabel(kop)}";
                case VariablePointer vp:
                    return $"variable {vp.variableId}";
                case ReferencePointer rp:
                    return $"reference {rp.valueId}";
                case StaticMemberPointer staticMember:
                    return $"staticMember {staticMember.memberId}";
                case CallFunctionPointer functionCall:
                    return $"functionCall {functionCall.memberId ?? functionCall.memberKey}";
                case CallDelegatePointer:
                    return "delegateCall";
                default:
                    return pointer.GetType().Name;
            }
        }

        private static string EvalPointerKeyLabel(KeyOfPointer pointer)
        {
            if (pointer.keyOf.key is ValuePointer valueKey
                && valueKey.value?.value?.Type == Newtonsoft.Json.Linq.JTokenType.String)
            {
                return valueKey.value.value.ToString();
            }
            return "<dynamic>";
        }

        private static object? EvalFunctionCall(
            CallFunctionPointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            if (ctx.functionCallHandler is not null)
            {
                return ctx.functionCallHandler(pointer, scope, ctx);
            }
            var receiver = EvalCallReceiver(pointer.receiver, scope, ctx);
            if (pointer.optional == true && receiver is null)
            {
                if (!pointer.receiver.IsStatic) return null;
            }
            var args = new object?[pointer.args.Length];
            for (int i = 0; i < pointer.args.Length; i++)
            {
                args[i] = EvalPointer(pointer.args[i], scope, ctx);
            }
            string memberId = ResolveFunctionMemberId(
                pointer,
                receiver,
                ctx);
            if (!ctx.client.TryGetMember(memberId, out JsonMember? member))
            {
                throw new NSGetterRuntimeError(
                    $"Function member '{memberId}' was not found.");
            }
            if (member is FunctionMember)
            {
                ctx.allocationTracker.ConsumeWorkUnit();
                return ctx.client.InvokeNativeFunction(memberId, receiver, args);
            }
            if (member is NSFunctionMember)
            {
                return NeoNSFunctionRuntime.InvokeImmediate(
                    ctx.client,
                    memberId,
                    receiver,
                    args,
                    ctx);
            }
            throw new NSGetterRuntimeError(
                $"Member '{memberId}' is not a callable Function member.");
        }

        /// <summary>
        /// Invokes a closure or bound callable-member target. Public so
        /// generated SDK surfaces and animation runtime code use the same
        /// dispatch as the <c>callDelegate</c> IR pointer.
        /// </summary>
        public static object? InvokeDelegate(
            object value,
            object?[] args,
            Context ctx)
        {
            NeoDelegateValue delegateValue = value switch
            {
                NeoDelegateValue typed => typed,
                JObject json => json.ToObject<NeoDelegateValue>()!,
                _ => throw new NSGetterRuntimeError(
                    "NeoDelegate value is neither a closure nor a bound member target."),
            };
            args ??= Array.Empty<object?>();
            if (delegateValue.IsClosure)
            {
                return InvokeDelegateClosure(delegateValue, args, ctx);
            }
            if (!delegateValue.IsMemberTarget)
            {
                throw new NSGetterRuntimeError(
                    "NeoDelegate value is neither a closure nor a bound member target.");
            }
            return InvokeDelegateMemberTarget(delegateValue, args, ctx);
        }

        private static object? InvokeDelegateClosure(
            NeoDelegateValue value,
            object?[] args,
            Context ctx)
        {
            FunctionWithReturnType action = value.action
                ?? throw new NSGetterRuntimeError(
                    "NeoDelegate closure has source code but no compiled action.");
            if (action.parameters is null || action.parameters.Length != args.Length + 2)
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate closure expects {Math.Max(0, action.parameters?.Length - 2 ?? 0)} arguments but received {args.Length}.");
            }
            object? lexicalThis = value.hasLexicalEnvironment
                ? value.lexicalThis
                : ctx.thisValue;
            object? lexicalRoot = value.hasLexicalEnvironment
                ? value.lexicalRoot
                : ctx.rootValue;
            var nestedCtx = ctx.WithThis(lexicalThis).WithRoot(lexicalRoot);
            var scope = new Dictionary<string, object?>(action.parameters.Length)
            {
                [action.parameters[0].id] = lexicalThis,
                [action.parameters[1].id] = lexicalRoot,
            };
            for (int i = 0; i < args.Length; i++)
            {
                scope[action.parameters[i + 2].id] =
                    NeoScriptValueMarshaller.Normalize(
                        ctx.client,
                        ctx.valueOwnership,
                        args[i],
                        action.parameters[i + 2].typeInfo,
                        nestedCtx,
                        $"argument {i} of NeoDelegate closure");
            }
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                ctx.client,
                action,
                scope,
                nestedCtx,
                NeoScriptExecutionOptions.ForImmediate(ctx.client));
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner("NeoDelegate closure suspended");
                throw new NSGetterRuntimeError(
                    "NeoDelegate closure suspended; delegate calls require an immediate callable target.");
            }
            return result.ReturnValue;
        }

        private static object? InvokeDelegateMemberTarget(
            NeoDelegateValue target,
            object?[] args,
            Context ctx)
        {
            string memberId = target.memberId!;
            if (!ctx.client.TryGetMember(memberId, out JsonMember? member))
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target member '{memberId}' does not exist.");
            }
            string frame = $"{member.name}[{target.valueId ?? "default"}]";
            if (ctx.delegateCallStack.Contains(frame))
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target cycle: {string.Join(" -> ", ctx.delegateCallStack.Concat(new[] { frame }))}.");
            }
            if (ctx.delegateCallStack.Count >= 64)
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate call stack exceeded 64 frames: {string.Join(" -> ", ctx.delegateCallStack.Concat(new[] { frame }))}.");
            }

            object? receiver = null;
            if (target.valueId is not null)
            {
                NeoValueOwnership ownership = ResolveOwnershipForValueId(
                    ctx,
                    target.valueId);
                if (!ctx.client.TryGetValue(
                        ownership,
                        target.valueId,
                        out MemberValue? row))
                {
                    throw new NSGetterRuntimeError(
                        $"NeoDelegate target '{member.name}' has missing receiver value '{target.valueId}'.");
                }
                receiver = UnwrapCached(row, ctx, ownership);
            }

            ctx.delegateCallStack.Add(frame);
            try
            {
                if (member is FunctionMember)
                {
                    if (ctx.client.IsNativeFunctionDeferred(memberId))
                    {
                        throw new NeoDeferredFunctionRuntimeError(
                            $"NeoDelegate target Function '{member.name}' is deferred; delegates require an immediate callable target.");
                    }
                    return ctx.client.InvokeNativeFunction(memberId, receiver, args);
                }
                if (member is NSFunctionMember)
                {
                    return NeoNSFunctionRuntime.InvokeImmediate(
                        ctx.client,
                        memberId,
                        receiver,
                        args,
                        ctx);
                }
                if (member is DelegateMember delegateMember)
                {
                    NeoDelegateValue nested = ResolveDelegateTargetValue(
                        delegateMember,
                        receiver,
                        ctx);
                    return InvokeDelegate(nested, args, ctx);
                }
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target '{member.name}' resolves to non-callable member kind {member.kind}.");
            }
            finally
            {
                ctx.delegateCallStack.RemoveAt(ctx.delegateCallStack.Count - 1);
            }
        }

        private static NeoDelegateValue ResolveDelegateTargetValue(
            DelegateMember member,
            object? receiver,
            Context ctx)
        {
            if (receiver is not null)
            {
                SchemaPlacement? placement = FindSchemaPlacementCached(
                    member.id,
                    ctx);
                if (placement is null
                    || receiver is not IDictionary<string, object?> record
                    || !record.TryGetValue(placement.schemaKey, out object? raw))
                {
                    throw new NSGetterRuntimeError(
                        $"NeoDelegate target '{member.name}' is missing from its receiver.");
                }
                object? resolved = ResolveValueIfId(
                    raw,
                    ctx,
                    member: member);
                if (resolved is NeoDelegateValue target) return target;
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target '{member.name}' has an invalid stored value.");
            }

            var visited = new HashSet<string>();
            DelegateMember? cursor = member;
            while (cursor is not null && visited.Add(cursor.id))
            {
                if (cursor.defaultValue?.value is NeoDelegateValue value)
                {
                    return value;
                }
                if (string.IsNullOrEmpty(cursor.extendsMemberId)
                    || !ctx.client.TryGetMember(
                        cursor.extendsMemberId,
                        out DelegateMember? parent))
                {
                    break;
                }
                cursor = parent;
            }
            throw new NSGetterRuntimeError(
                $"NeoDelegate target '{member.name}' has no declaration default.");
        }

        internal static string ResolveFunctionMemberId(
            CallFunctionPointer pointer,
            object? receiver,
            Context ctx)
        {
            if (pointer.receiver.IsStatic)
            {
                string targetMemberId = pointer.memberId
                    ?? pointer.receiver.memberId
                    ?? throw new NSGetterRuntimeError(
                        "Static Function call is missing its callable member id.");
                ValidateStaticCallableReceiver(
                    pointer.receiver,
                    targetMemberId,
                    "Function",
                    ctx);
                if (!string.IsNullOrEmpty(pointer.memberKey))
                {
                    throw new NSGetterRuntimeError(
                        "Static Function call must dispatch by memberId, not memberKey.");
                }
                return targetMemberId;
            }
            string? schemaKey = pointer.memberKey;
            if (string.IsNullOrEmpty(schemaKey)
                && !string.IsNullOrEmpty(pointer.memberId))
            {
                SchemaPlacement? placement = FindSchemaPlacementCached(
                    pointer.memberId!, ctx);
                if (placement is null) return pointer.memberId!;
                schemaKey = placement.schemaKey;
            }
            if (string.IsNullOrEmpty(schemaKey))
            {
                throw new NSGetterRuntimeError(
                    "Function call is missing both memberId and memberKey.");
            }

            string? runtimeClassId = FindRowClassIdByReference(receiver, ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
            {
                if (!string.IsNullOrEmpty(pointer.memberId))
                {
                    return pointer.memberId!;
                }
                throw new NSGetterRuntimeError(
                    $"Cannot resolve interface Function member '{schemaKey}' because the receiver has no runtime class.");
            }

            string dispatchCacheKey = runtimeClassId + "\n" + schemaKey;
            if (ctx.callableDispatchCache.TryGetValue(
                    dispatchCacheKey, out string? cachedMemberId))
            {
                if (cachedMemberId is null)
                {
                    throw new NSGetterRuntimeError(
                        $"Runtime class '{runtimeClassId}' does not implement Function member '{schemaKey}'.");
                }
                return cachedMemberId;
            }

            try
            {
                ctx.client.ResolveClassInheritanceChain(runtimeClassId!);
            }
            catch (CircularInheritanceError)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot resolve Function member '{schemaKey}' because runtime class '{runtimeClassId}' has circular inheritance.");
            }
            foreach (MergedSchemaEntry entry in
                ctx.client.ResolveInstanceSurfaceSchema(runtimeClassId!))
            {
                if (entry.schemaKey != schemaKey) continue;
                if (!TryResolveCallableKind(ctx.client, entry.memberId))
                {
                    throw new NSGetterRuntimeError(
                        $"Runtime class '{runtimeClassId}' member '{schemaKey}' is not a Function member.");
                }
                ctx.callableDispatchCache[dispatchCacheKey] = entry.memberId;
                return entry.memberId;
            }
            ctx.callableDispatchCache[dispatchCacheKey] = null;
            throw new NSGetterRuntimeError(
                $"Runtime class '{runtimeClassId}' does not implement Function member '{schemaKey}'.");
        }

        internal static object? EvalCallReceiver(
            CallReceiver receiver,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            if (receiver is null)
            {
                throw new NSGetterRuntimeError("Callable pointer is missing its receiver.");
            }
            if (receiver.IsStatic) return null;
            if (receiver.kind != CallReceiverKind.Instance || receiver.pointer is null)
            {
                throw new NSGetterRuntimeError(
                    $"Unsupported call receiver kind '{receiver.kind ?? "<missing>"}'.");
            }
            return EvalPointer(receiver.pointer, scope, ctx);
        }

        private static void ValidateStaticCallableReceiver(
            CallReceiver receiver,
            string targetMemberId,
            string callableKind,
            Context ctx)
        {
            if (string.IsNullOrEmpty(receiver.memberId))
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} call receiver is missing its member id.");
            }
            if (receiver.memberId != targetMemberId)
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} call receiver '{receiver.memberId}' does not match target '{targetMemberId}'.");
            }
            if (!ctx.client.TryGetMember(
                    targetMemberId,
                    out JsonMember? member)
                || !member.isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} target '{targetMemberId}' is missing or is not static.");
            }
        }

        private static bool EvalFunctionErrorCheck(
            FunctionErrorCheckPointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            try
            {
                EvalFunctionCall(pointer.call, scope, ctx);
                return pointer.mode == FunctionErrorCheckKind.DoesNotThrow;
            }
            catch (NeoDeferredFunctionRuntimeError)
            {
                throw;
            }
            catch (NeoFunctionCallSuspended)
            {
                throw;
            }
            catch (NeoScriptResourceLimitError)
            {
                throw;
            }
            catch
            {
                return pointer.mode == FunctionErrorCheckKind.Throws;
            }
        }

        private static bool TryResolveCallableKind(NeoClient client, string memberId)
        {
            var visited = new HashSet<string>();
            string? cursor = memberId;
            MemberKind? expectedType = null;
            while (!string.IsNullOrEmpty(cursor) && visited.Add(cursor))
            {
                if (!client.TryGetMember(cursor!, out JsonMember? member)) return false;
                expectedType ??= member.kind;
                if (member.kind != expectedType
                    || (member.kind != MemberKind.Function
                        && member.kind != MemberKind.NSFunction))
                {
                    return false;
                }
                if (member is FunctionMember or NSFunctionMember) return true;
                cursor = member.extendsMemberId;
            }
            return false;
        }

        private static SchemaPlacement? FindSchemaPlacementCached(
            string memberId,
            Context ctx)
        {
            if (ctx.schemaPlacementCache.TryGetValue(
                    memberId, out SchemaPlacement? cached))
            {
                return cached;
            }
            SchemaPlacement? placement = NeoSchemaClassInheritance.FindSchemaPlacement(
                memberId,
                EnumerateClasses(ctx.client));
            ctx.schemaPlacementCache[memberId] = placement;
            return placement;
        }

        // ---------------------------------------------------------------
        // KeyOf — schema-key dispatch with runtime-classId override hook
        // ---------------------------------------------------------------

        private static object? EvalKeyOf(
            KeyOf keyOf,
            Dictionary<string, object?> scope,
            Context ctx,
            bool optional,
            string? pinnedMemberId)
        {
            var receiver = EvalPointer(keyOf.pointer, scope, ctx);
            if (optional && receiver is null) return null;
            receiver = UnwrapGeneratedValue(receiver, ctx);
            var key = EvalPointer(keyOf.key, scope, ctx);
            if (receiver is null)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot read property '{key}' of null");
            }

            // List indexing: numeric keys are positional; String keys are
            // exact stable value ids (including numeric-looking strings).
            if (receiver is object?[] arr)
            {
                if (key is string valueId)
                {
                    Dictionary<string, int> identity = ListIdentityIndexes.GetValue(
                        arr,
                        entries => BuildListIdentityIndex((object?[])entries));
                    if (!identity.TryGetValue(valueId, out int valueIndex))
                    {
                        throw new NSGetterRuntimeError(
                            $"Value id '{valueId}' is not a member of this List");
                    }
                    return ResolveValueIfId(
                        arr[valueIndex],
                        ctx,
                        FindRowOwnershipByReference(receiver, ctx));
                }
                int idx = ToIntKey(key);
                if (idx < 0 || idx >= arr.Length)
                {
                    throw new NSGetterRuntimeError(
                        $"List index out of bounds: {key}");
                }
                return ResolveValueIfId(arr[idx], ctx, FindRowOwnershipByReference(receiver, ctx));
            }

            string k = key?.ToString() ?? "null";
            if (TryReadVectorComponent(receiver, k, out float component))
            {
                return component;
            }
            // P42 §3. Colour channels read exactly like vector components.
            // Before P42 a `ColorMemberValue` unwrapped to a bare
            // `NeoColorValue`, which is neither a vector nor an
            // `IDictionary`, so `Tint.a` fell through to the "cannot index
            // into" throw below while the TS evaluator read it happily.
            if (TryReadColorComponent(receiver, k, out float channel))
            {
                return channel;
            }
            if (k == "Id")
            {
                if (receiver is INeoValueReference reference
                    && !string.IsNullOrEmpty(reference.valueId))
                {
                    return reference.valueId;
                }
                string? rowId = FindRowIdByReference(receiver, ctx);
                if (!string.IsNullOrEmpty(rowId))
                {
                    return rowId;
                }
                throw new NSGetterRuntimeError("Class value has no backing row id.");
            }

            // Dict / Class record: receiver is Dictionary<string, ...>.
            if (TryAsObjectRecord(receiver, out IDictionary<string, object?>? record))
            {
                // Schema-dispatch if the receiver is a tracked Class row.
                var dispatched = DispatchSchemaMember(
                    receiver,
                    k,
                    ctx);
                if (dispatched.kind == DispatchKind.Ok) return dispatched.value;
                // Interface/static-type pointers retain the compile-time
                // declaration id. Use it only when the concrete runtime Class
                // had no member at this key; a concrete stored override must
                // remain authoritative over a read-only base declaration.
                if (!dispatched.matchedMember
                    && !string.IsNullOrEmpty(pinnedMemberId)
                    && ctx.client.TryGetMember(pinnedMemberId!, out JsonMember? pinnedMember))
                {
                    DispatchResult pinnedDefault = ReadOnlyDeclarationDefault(
                        pinnedMember,
                        ctx);
                    if (pinnedDefault.kind == DispatchKind.Ok)
                    {
                        return pinnedDefault.value;
                    }
                }
                if (record!.TryGetValue(k, out var at))
                {
                    return ResolveValueIfId(at, ctx, FindRowOwnershipByReference(receiver, ctx));
                }
                throw new NSGetterRuntimeError($"Missing key '{k}' on object");
            }

            throw new NSGetterRuntimeError(
                $"Cannot index into {ReceiverTypeName(receiver)} with key '{key}'");
        }

        private static Dictionary<string, int> BuildListIdentityIndex(object?[] entries)
        {
            var index = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] is not string valueId)
                {
                    throw new NSGetterRuntimeError(
                        $"Schema-backed List entry at position {i} has no stable String value id");
                }
                if (!index.TryAdd(valueId, i))
                {
                    throw new NSGetterRuntimeError(
                        $"Schema-backed List contains duplicate value id '{valueId}'");
                }
            }
            return index;
        }

        private enum DispatchKind { Ok, NoInfo }
        private readonly struct DispatchResult
        {
            public DispatchKind kind { get; }
            public object? value { get; }
            public bool matchedMember { get; }
            public DispatchResult(DispatchKind kind, object? value, bool matchedMember)
            {
                this.kind = kind;
                this.value = value;
                this.matchedMember = matchedMember;
            }
            public static DispatchResult Ok(object? v) => new(DispatchKind.Ok, v, true);
            public static DispatchResult NoInfo(bool matchedMember = false) =>
                new(DispatchKind.NoInfo, null, matchedMember);
        }

        /// <summary>
        /// Runtime member-access dispatch on a Class record. Mirrors
        /// the TS-side <c>dispatchSchemaMember</c>. Recovers the
        /// receiver's runtime <c>classId</c> by reference-equality
        /// against tracked rows, walks the merged schema for that
        /// type, and dispatches to either an NSProperty (if the merged
        /// entry is one and has a compiled getter) or a stored-field
        /// read.
        /// </summary>
        private static DispatchResult DispatchSchemaMember(
            object? receiver,
            string schemaKey,
            Context ctx)
        {
            if (!TryAsObjectRecord(receiver, out IDictionary<string, object?>? record))
            {
                return DispatchResult.NoInfo();
            }

            // Recover the row by reference equality on `.value`.
            string? runtimeClassId = FindRowClassIdByReference(receiver, ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
            {
                return DispatchResult.NoInfo();
            }

            MergedSchemaEntry? entry = null;
            IList<NeoSchemaClass>? runtimeChain = null;
            try
            {
                runtimeChain = ctx.client.ResolveClassInheritanceChain(runtimeClassId!);
                foreach (var candidate in ctx.client.ResolveInstanceSurfaceSchema(runtimeClassId!))
                {
                    if (candidate.schemaKey == schemaKey)
                    {
                        entry = candidate;
                        break;
                    }
                }
            }
            catch (CircularInheritanceError)
            {
                return DispatchResult.NoInfo();
            }

            if (entry is null
                || !ctx.client.TryGetMember(entry.memberId, out JsonMember? member))
            {
                return DispatchResult.NoInfo();
            }

            if (member is GenericMember && runtimeChain is not null)
            {
                member = NeoGenericResolution.SubstituteMember(
                    ctx.client,
                    member,
                    NeoGenericResolution.ResolveEnv(runtimeChain));
            }

            DispatchResult declarationDefault = ReadOnlyDeclarationDefault(member, ctx);
            if (declarationDefault.kind == DispatchKind.Ok)
            {
                return declarationDefault;
            }

            if (member.kind == MemberKind.NSProperty)
            {
                if (ResolveCompiledGetter(entry.memberId, ctx.client) is null)
                {
                    return DispatchResult.NoInfo(matchedMember: true);
                }
                return DispatchResult.Ok(DispatchNSGetterById(entry.memberId, receiver, ctx));
            }

            if (record!.TryGetValue(schemaKey, out var at))
            {
                return DispatchResult.Ok(
                    ResolveValueIfId(at, ctx, FindRowOwnershipByReference(receiver, ctx), member));
            }
            return DispatchResult.NoInfo(matchedMember: true);
        }

        private static DispatchResult ReadOnlyDeclarationDefault(
            JsonMember member,
            Context ctx)
        {
            if (member.isReadOnly != true)
            {
                return DispatchResult.NoInfo(matchedMember: true);
            }
            MemberValue? synthetic = ctx.client.CreateDeclarationDefaultValue(
                member,
                $"__neo_readonly_default:{member.RuntimeDeclarationIdentity}");
            if (synthetic is null)
            {
                throw new NSGetterRuntimeError(
                    $"Read-only member '{member.name}' ({member.id}) has no declaration default.");
            }
            object? unwrapped = UnwrapCached(
                synthetic,
                ctx,
                NeoValueOwnership.Asset,
                member);
            if (member is LookupMember lookup
                && !lookup.multiselect
                && unwrapped is object?[] selections
                && selections.Length == 1
                && selections[0] is string selectedId)
            {
                return DispatchResult.Ok(ResolveValueIfId(selectedId, ctx));
            }
            return DispatchResult.Ok(unwrapped);
        }

        /// <summary>
        /// Cycle-checked recursive evaluation of an NSProperty member by
        /// id. Walks <c>extendsMemberId</c> for the first compiled
        /// <c>getter</c>, then runs it with the receiver as
        /// <c>__this__</c>.
        /// </summary>
        private static object? DispatchNSGetterById(
            string memberId,
            object? receiver,
            Context ctx)
        {
            if (ctx.getterCallStack.Contains(memberId))
            {
                throw new NSGetterRuntimeError(
                    $"Circular getter call: member '{memberId}' is already being evaluated");
            }
            var getter = ResolveCompiledGetter(memberId, ctx.client);
            if (getter is null)
            {
                string name = ctx.client.TryGetMember(memberId, out JsonMember? member)
                    ? member.name
                    : memberId;
                throw new NSGetterRuntimeError(
                    $"Getter '{name}' has no compiled `getter` — save its code to compile it");
            }
            var inner = ctx.WithGetterPushed(memberId).WithThis(receiver);
            return Evaluate(getter, inner);
        }

        private static FunctionWithReturnType? ResolveCompiledGetter(
            string memberId, NeoClient client)
        {
            return NeoSchemaClassInheritance.WalkExtendsMemberChain(
                memberId,
                id => client.TryGetMember(id, out JsonMember? a) ? a : null,
                a => a is NSPropertyMember ng ? ng.getter : null,
                requireKind: MemberKind.NSProperty);
        }

        // ---------------------------------------------------------------
        // Operations
        // ---------------------------------------------------------------

        private static object? EvalOperation(
            Operation operation,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            switch (operation)
            {
                case ArithmeticOperation arith:
                {
                    var info = arith.arithmetic;
                    var operands = new object?[info.pointers.Length];
                    for (int i = 0; i < info.pointers.Length; i++)
                    {
                        operands[i] = EvalPointer(info.pointers[i], scope, ctx);
                    }
                    return ApplyArithmetic(
                        info.type,
                        operands,
                        info.isDecimal == true,
                        ctx);
                }
                case BooleanOperation boolOp:
                    return EvalBooleanExpression(boolOp.expression, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown operation kind {operation.GetType().Name}");
            }
        }

        private static object? ApplyArithmetic(
            string op,
            object?[] operands,
            bool isDecimal,
            Context ctx)
        {
            if (operands.Length == 0)
            {
                throw new NSGetterRuntimeError("Arithmetic operation with no operands");
            }
            // Decimal-stamped operations route to exact math BEFORE the
            // string dispatch below — decimal runtime values are canonical
            // strings, and without this branch `+` would concatenate them.
            if (isDecimal)
            {
                return ApplyDecimalArithmetic(op, operands);
            }
            // String concat for `+` over all-strings.
            if (op == ArithmeticOpKind.Addition)
            {
                bool allStrings = true;
                foreach (var o in operands) { if (o is not string) { allStrings = false; break; } }
                if (allStrings)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var o in operands) sb.Append((string)o!);
                    string result = sb.ToString();
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
                bool anyString = false;
                foreach (var o in operands) { if (o is string) { anyString = true; break; } }
                if (anyString)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var o in operands) sb.Append(StringifyForInterp(o));
                    string result = sb.ToString();
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
            }
            // Numeric path. Coerce every operand to double; ints round-trip.
            var nums = new double[operands.Length];
            for (int i = 0; i < operands.Length; i++)
            {
                if (!TryAsDouble(operands[i], out double d))
                {
                    throw new NSGetterRuntimeError(
                        $"Arithmetic operand is not numeric: {ReceiverTypeName(operands[i])}");
                }
                nums[i] = d;
            }
            switch (op)
            {
                case ArithmeticOpKind.Addition:
                {
                    double sum = 0;
                    foreach (var n in nums) sum += n;
                    return sum;
                }
                case ArithmeticOpKind.Subtraction:
                {
                    double r = nums[0];
                    for (int i = 1; i < nums.Length; i++) r -= nums[i];
                    return r;
                }
                case ArithmeticOpKind.Multiplication:
                {
                    double r = 1;
                    foreach (var n in nums) r *= n;
                    return r;
                }
                case ArithmeticOpKind.Division:
                {
                    double r = nums[0];
                    for (int i = 1; i < nums.Length; i++)
                    {
                        if (nums[i] == 0) throw new NSGetterRuntimeError("Division by zero");
                        r /= nums[i];
                    }
                    return r;
                }
                case ArithmeticOpKind.Remainder:
                {
                    double r = nums[0];
                    for (int i = 1; i < nums.Length; i++)
                    {
                        if (nums[i] == 0) throw new NSGetterRuntimeError("Modulo by zero");
                        r %= nums[i];
                    }
                    return r;
                }
                default:
                    throw new NSGetterRuntimeError($"Unknown arithmetic op '{op}'");
            }
        }

        // ---------------------------------------------------------------
        // Decimal support (specs/decimal-member.md decision 7 / §6.4).
        // Decimal values travel through the evaluator as canonical decimal
        // strings; all math routes through NeoDecimalMath (the BigInteger
        // core shared with the web's decimal-math.ts via the parity
        // fixture) — native System.Decimal arithmetic is never used here.
        // ---------------------------------------------------------------

        /// <summary>
        /// True when <paramref name="value"/> is a runtime number — the C#
        /// analog of the TS evaluator's <c>typeof value === "number"</c>
        /// guard at the Int→Decimal widening seams.
        /// </summary>
        private static bool IsRuntimeNumber(object? value)
        {
            return value is double or float or int or long or short;
        }

        /// <summary>
        /// Coerces a decimal-stamped operand to a canonical decimal string
        /// (mirror of the TS evaluator's <c>coerceDecimalOperand</c>).
        /// Integer numbers widen exactly (`int` operands in mixed
        /// expressions); strings must already be canonical (they always are
        /// when produced by decimal-typed pointers or ops). Internal so
        /// <see cref="NeoDialogueActionEvaluator"/> reuses the identical
        /// seam for EditMember assignment.
        /// </summary>
        internal static string CoerceDecimalOperand(object? value, string context)
        {
            if (value is string text)
            {
                if (NeoDecimalValues.GetViolation(text) != NeoDecimalValues.Violation.None)
                {
                    throw new NSGetterRuntimeError(
                        $"Decimal {context} operand is not a canonical decimal string: \"{text}\"");
                }
                return text;
            }
            if (TryAsDouble(value, out double number))
            {
                if (double.IsNaN(number))
                {
                    throw new NSGetterRuntimeError(
                        $"Decimal {context} operand is NaN; convert explicitly with ToDecimal(digits).");
                }
                if (double.IsInfinity(number))
                {
                    throw new NSGetterRuntimeError(
                        $"Decimal {context} operand is not finite; convert explicitly with ToDecimal(digits).");
                }
                if (number != System.Math.Truncate(number))
                {
                    throw new NSGetterRuntimeError(
                        $"Decimal {context} operand {number.ToString(CultureInfo.InvariantCulture)} is not an integer; convert explicitly with ToDecimal(digits).");
                }
                if (System.Math.Abs(number) > 9007199254740991d)
                {
                    throw new NSGetterRuntimeError(
                        $"Decimal {context} operand {number.ToString(CultureInfo.InvariantCulture)} exceeds the exactly-representable integer range; convert explicitly with ToDecimal(digits).");
                }
                return ((long)number).ToString(CultureInfo.InvariantCulture);
            }
            throw new NSGetterRuntimeError(
                $"Decimal {context} operand is not numeric: {ReceiverTypeName(value)}");
        }

        private static string ApplyDecimalArithmetic(string op, object?[] operands)
        {
            var decimals = new string[operands.Length];
            for (int i = 0; i < operands.Length; i++)
            {
                decimals[i] = CoerceDecimalOperand(operands[i], "arithmetic");
            }
            try
            {
                switch (op)
                {
                    case ArithmeticOpKind.Addition:
                    {
                        string acc = decimals[0];
                        for (int i = 1; i < decimals.Length; i++) acc = NeoDecimalMath.Add(acc, decimals[i]);
                        return acc;
                    }
                    case ArithmeticOpKind.Subtraction:
                    {
                        string acc = decimals[0];
                        for (int i = 1; i < decimals.Length; i++) acc = NeoDecimalMath.Subtract(acc, decimals[i]);
                        return acc;
                    }
                    case ArithmeticOpKind.Multiplication:
                    {
                        string acc = decimals[0];
                        for (int i = 1; i < decimals.Length; i++) acc = NeoDecimalMath.Multiply(acc, decimals[i]);
                        return acc;
                    }
                    default:
                        // Unreachable for `/` and `%`: the compiler rejects
                        // them on decimals (specs/decimal-member.md
                        // decision 7).
                        throw new NSGetterRuntimeError(
                            $"Decimal '{op}' is not supported at runtime.");
                }
            }
            catch (DecimalOverflowException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
        }

        private static bool EvalBooleanExpression(
            BooleanExpression expression,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            bool head = EvalCondition(expression.condition, scope, ctx);
            if (expression.connective is null) return head;
            bool tail = EvalBooleanExpression(expression.connective.to, scope, ctx);
            switch (expression.connective.type)
            {
                case LogicalOpKind.And: return head && tail;
                case LogicalOpKind.Or: return head || tail;
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown logical operator '{expression.connective.type}'");
            }
        }

        private static bool EvalCondition(
            Condition condition,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var a = EvalPointer(condition.operand1, scope, ctx);
            var b = EvalPointer(condition.operand2, scope, ctx);
            // Decimal-stamped comparisons are exact and scale-blind
            // ("1.10" == "1.1"). Null operands (optional decimals) keep the
            // JsEqual null semantics for equality; ordering against null is
            // a runtime error (TS evaluator parity).
            if (condition.isDecimal == true)
            {
                bool aIsNull = a is null;
                bool bIsNull = b is null;
                if (aIsNull || bIsNull)
                {
                    switch (condition.type)
                    {
                        case OperatorKind.EqualTo: return aIsNull && bIsNull;
                        case OperatorKind.DoesNotEqual: return !(aIsNull && bIsNull);
                        default:
                            throw new NSGetterRuntimeError(
                                "Decimal ordering comparison received a null operand.");
                    }
                }
                int comparison = NeoDecimalMath.Compare(
                    CoerceDecimalOperand(a, "comparison"),
                    CoerceDecimalOperand(b, "comparison"));
                switch (condition.type)
                {
                    case OperatorKind.EqualTo: return comparison == 0;
                    case OperatorKind.DoesNotEqual: return comparison != 0;
                    case OperatorKind.GreaterThan: return comparison > 0;
                    case OperatorKind.GreaterThanOrEqualTo: return comparison >= 0;
                    case OperatorKind.LessThan: return comparison < 0;
                    case OperatorKind.LessThanOrEqualTo: return comparison <= 0;
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown comparison operator '{condition.type}'");
                }
            }
            switch (condition.type)
            {
                case OperatorKind.EqualTo: return JsEqual(a, b);
                case OperatorKind.DoesNotEqual: return !JsEqual(a, b);
                case OperatorKind.GreaterThan: return NumericCompare(a, b) > 0;
                case OperatorKind.GreaterThanOrEqualTo: return NumericCompare(a, b) >= 0;
                case OperatorKind.LessThan: return NumericCompare(a, b) < 0;
                case OperatorKind.LessThanOrEqualTo: return NumericCompare(a, b) <= 0;
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown comparison operator '{condition.type}'");
            }
        }

        // ---------------------------------------------------------------
        // Functions — 6 kinds
        // ---------------------------------------------------------------

        /// <summary>
        /// P43 §6.1 — evaluates <c>new Foo(Named: …) { X = … }</c> against a
        /// class that declares constructors.
        ///
        /// <para>Every piece of metadata — the class, the merged schema behind
        /// the call-site fields, the resolved overload, its parameter names,
        /// and the whole base chain — is validated before a single argument or
        /// field expression runs, so stale IR cannot trigger argument side
        /// effects. This is the same ordering invariant the
        /// <c>classConstructor</c> arm establishes.</para>
        /// </summary>
        private static object? EvalDeclaredConstructor(
            DeclaredConstructorInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var fields = new List<NeoGeneratedTypesSupport.RuntimeConstructorField>(
                info.fields.Length);
            foreach (FunctionClassConstructorField field in info.fields)
            {
                fields.Add(new NeoGeneratedTypesSupport.RuntimeConstructorField
                {
                    schemaKey = field.schemaKey,
                    memberId = field.memberId,
                });
            }
            var argumentNames = new List<string>(info.args.Length);
            foreach (DeclaredConstructorArgument argument in info.args)
            {
                argumentNames.Add(argument.name);
            }

            NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved;
            try
            {
                resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(
                    ctx.client,
                    info.schemaClassInfo,
                    info.constructorId,
                    argumentNames,
                    fields);
            }
            catch (Exception error)
                when (error is InvalidOperationException
                    || error is ArgumentException)
            {
                throw new NSGetterRuntimeError(
                    $"Declared constructor failed: {error.Message}");
            }

            var argumentValues = new Dictionary<string, object?>(info.args.Length);
            foreach (DeclaredConstructorArgument argument in info.args)
            {
                argumentValues[argument.name] = EvalPointer(
                    argument.valuePointer,
                    scope,
                    ctx);
            }

            try
            {
                NeoMemberClassWritable node =
                    NeoGeneratedTypesSupport.ConstructDeclaredClassValue(
                        resolved,
                        argumentValues,
                        fields,
                        ctx,
                        // P43 §6.1 step 4 — the call-site initializer block is
                        // evaluated AFTER the body, as in C# where an object
                        // initializer's expressions run once the constructor
                        // has returned. Handing construction a thunk instead of
                        // pre-evaluated values is what keeps that order:
                        // evaluating here would make a field expression read
                        // pre-body state.
                        constructionCtx =>
                        {
                            for (int i = 0; i < fields.Count; i++)
                            {
                                fields[i].value = EvalPointer(
                                    info.fields[i].valuePointer,
                                    scope,
                                    constructionCtx);
                            }
                        });
                if (node.value is null)
                {
                    throw new NSGetterRuntimeError(
                        $"Declared constructor for '{info.schemaClassInfo.classId}' produced no root row.");
                }
                ctx.allocationTracker.RegisterSessionRoot(node.value.id);
                return UnwrapCached(
                    node.value,
                    ctx,
                    NeoValueOwnership.Session,
                    node.member);
            }
            catch (Exception error)
                when (error is InvalidOperationException
                    || error is ArgumentException)
            {
                throw new NSGetterRuntimeError(
                    $"Declared constructor failed: {error.Message}");
            }
        }

        /// <summary>
        /// The construction-frame label for a class id: its schema name, which
        /// is what the TypeScript evaluator pushes and therefore what the
        /// shared depth-cap diagnostic prints. Falls back to the id when the
        /// class is unresolvable, so a diagnostic never becomes a second
        /// failure.
        /// </summary>
        private static string ConstructedClassLabel(Context ctx, string classId)
        {
            return ctx.client.TryGetClass(classId, out NeoSchemaClass? schemaClass)
                ? schemaClass!.name
                : classId;
        }

        private static object? EvalFunction(
            Function fn,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            switch (fn)
            {
                case ClassConstructorFunction constructor:
                {
                    var fields = new List<NeoGeneratedTypesSupport.RuntimeConstructorField>(
                        constructor.info.fields.Length);
                    foreach (FunctionClassConstructorField field in constructor.info.fields)
                    {
                        fields.Add(new NeoGeneratedTypesSupport.RuntimeConstructorField
                        {
                            schemaKey = field.schemaKey,
                            memberId = field.memberId,
                        });
                    }
                    try
                    {
                        NeoGeneratedTypesSupport.ValidateRuntimeClassConstructorMetadata(
                            ctx.client,
                            constructor.info.schemaClassInfo,
                            fields);
                    }
                    catch (Exception error)
                        when (error is InvalidOperationException
                            || error is ArgumentException)
                    {
                        throw new NSGetterRuntimeError(
                            $"Class constructor failed: {error.Message}");
                    }
                    // P43 §7.2.3 — the schema-derived arm is a construction
                    // too, so it opens its own frame before any field runs,
                    // exactly where `constructClassValue` opens one in
                    // evaluateNSGetter.ts. The pushed context is then threaded
                    // into the materializer, so a member initializer met while
                    // filling defaults counts against the SAME cap instead of
                    // starting a fresh stack that can never trip.
                    Context constructionCtx =
                        NeoGeneratedTypesSupport.PushConstructionFrame(
                            ctx,
                            ConstructedClassLabel(
                                ctx,
                                constructor.info.schemaClassInfo.classId));
                    for (int i = 0; i < fields.Count; i++)
                    {
                        // Deliberately eval-first, unlike the declared arm:
                        // this IR has no body, so there is nothing for a field
                        // expression to observe, and both runtimes pin the
                        // legacy order here.
                        fields[i].value = EvalPointer(
                            constructor.info.fields[i].valuePointer,
                            scope,
                            constructionCtx);
                    }
                    try
                    {
                        NeoMemberClassWritable node =
                            NeoGeneratedTypesSupport.CreateRuntimeClassValue(
                                ctx.client,
                                constructor.info.schemaClassInfo,
                                fields,
                                value => ConstructorReferenceOf(value, constructionCtx),
                                constructionCtx);
                        if (node.value is null)
                        {
                            throw new NSGetterRuntimeError(
                                $"Class constructor for '{constructor.info.schemaClassInfo.classId}' produced no root row.");
                        }
                        ctx.allocationTracker.RegisterSessionRoot(node.value.id);
                        return UnwrapCached(
                            node.value,
                            ctx,
                            NeoValueOwnership.Session,
                            node.member);
                    }
                    catch (Exception error)
                        when (error is InvalidOperationException
                            || error is ArgumentException)
                    {
                        throw new NSGetterRuntimeError(
                            $"Class constructor failed: {error.Message}");
                    }
                }
                case DeclaredConstructorFunction declared:
                    return EvalDeclaredConstructor(declared.info, scope, ctx);
                case ClassCloneFunction ccf:
                {
                    var receiver = EvalPointer(ccf.info.receiverPointer, scope, ctx);
                    if (receiver is null)
                    {
                        throw new NSGetterRuntimeError(
                            "Class.Clone receiver is null; narrow or force-unwrap the optional value first.");
                    }
                    if (!TryFindRowReferenceByReference(receiver, ctx, out RowReference source))
                    {
                        throw new NSGetterRuntimeError(
                            "Class.Clone receiver has no backing value row.");
                    }
                    try
                    {
                        var existingSessionIds = new HashSet<string>(
                            ctx.client.sessionValues.Keys);
                        string cloneId = ctx.client.CloneValueReference(
                            source.valueId,
                            source.ownership);
                        ctx.allocationTracker.RegisterSessionRoot(cloneId);
                        var createdRows = new List<MemberValue>();
                        foreach (var pair in ctx.client.sessionValues)
                        {
                            if (!existingSessionIds.Contains(pair.Key))
                            {
                                createdRows.Add(pair.Value);
                            }
                        }
                        ctx.allocationTracker.ConsumeCreatedSessionRows(
                            createdRows);
                        if (!ctx.client.TryGetValue(
                                NeoValueOwnership.Session,
                                cloneId,
                                out MemberValue? cloneRow))
                        {
                            throw new NSGetterRuntimeError(
                                $"Class.Clone created value '{cloneId}', but its Session row could not be read.");
                        }
                        return UnwrapCached(cloneRow, ctx, NeoValueOwnership.Session);
                    }
                    catch (InvalidOperationException error)
                    {
                        throw new NSGetterRuntimeError(
                            $"Class.Clone failed for value '{source.valueId}': {error.Message}");
                    }
                }
                case VisitCountFunction vcf:
                {
                    var pointer = EvalPointer(vcf.info.pointer, scope, ctx);
                    return pointer is string text
                        ? NeoDialogueMemoryQueries.VisitCount(ctx.memoryStore, text)
                        : 0;
                }
                case HasVisitedFunction hvf:
                {
                    var pointer = EvalPointer(hvf.info.pointer, scope, ctx);
                    return pointer is string text
                        && NeoDialogueMemoryQueries.HasVisited(ctx.memoryStore, text);
                }
                case VectorConstructorFunction vcf:
                    return EvalVectorConstructor(vcf.info, scope, ctx);
                case ImageSliceFunction isf:
                    return EvalImageSlice(isf.info, scope, ctx);
                case DecimalOpFunction dof:
                    return EvalDecimalOp(dof.info, scope, ctx);
                case StringOpFunction sof:
                    return EvalStringOp(sof.info, scope, ctx);
                case ListIndexFunction lif:
                    return EvalListIndex(lif.info, scope, ctx);
                case CountFunction cf:
                {
                    var c = EvalPointer(cf.info.collectionPointer, scope, ctx);
                    return CollectionLength(c);
                }
                case ContainsFunction cnf:
                {
                    var c = EvalPointer(cnf.info.collectionPointer, scope, ctx);
                    var target = EvalPointer(cnf.info.valuePointer, scope, ctx);
                    if (c is string s)
                    {
                        if (target is not string ts)
                        {
                            throw new NSGetterRuntimeError(
                                "string.Contains argument must be a string");
                        }
                        return s.Contains(ts);
                    }
                    if (c is object?[] raw && target is string targetId)
                    {
                        foreach (var entry in raw)
                        {
                            ctx.allocationTracker.ConsumeCollectionVisit();
                            if (entry is string selectedId && selectedId == targetId)
                            {
                                return true;
                            }
                        }
                    }
                    if (c is object?[] rawWithReference
                        && ValueIdOf(target, ctx) is string targetReferenceId)
                    {
                        foreach (var entry in rawWithReference)
                        {
                            ctx.allocationTracker.ConsumeCollectionVisit();
                            if (entry is string selectedId && selectedId == targetReferenceId)
                            {
                                return true;
                            }
                        }
                    }
                    foreach (var entry in CollectionEntries(c, ctx))
                    {
                        if (JsEqual(entry, target)) return true;
                    }
                    return false;
                }
                case WhereFunction wf:
                {
                    var c = EvalPointer(wf.info.collectionPointer, scope, ctx);
                    var inner = wf.info.function;
                    bool isList = c is object?[];
                    object outAcc = isList
                        ? (object)new List<object?>()
                        : new Dictionary<string, object?>();
                    IterateCollection(c, ctx, (entry, key, valueId) =>
                    {
                        var innerScope = PushParams(scope, inner.parameters,
                            keyOrIndex: key, entry: entry, isList: isList);
                        NeoScriptExecutionResult result = ExecuteCollectionCallback(
                            inner,
                            innerScope,
                            ctx);
                        if (result.Returned && result.ReturnValue is bool b && b)
                        {
                            ctx.allocationTracker
                                .ConsumeProducedCollectionEntry();
                            // Re-emit valueId references rather than dereferenced
                            // entries when we have them — matches TS semantic.
                            object? emit = valueId is null ? entry : valueId;
                            if (isList) ((List<object?>)outAcc).Add(emit);
                            else ((Dictionary<string, object?>)outAcc)[key.ToString()!] = emit;
                        }
                    });
                    if (isList) return ((List<object?>)outAcc).ToArray();
                    return outAcc;
                }
                case FirstFunction _:
                case FirstOrDefaultFunction _:
                {
                    // Both share the optional-predicate shape. Switch on the
                    // function class to choose throw-vs-null on no-match.
                    bool isFirst = fn is FirstFunction;
                    FunctionCollectionOptionalBoolInfo info = isFirst
                        ? ((FirstFunction)fn).info
                        : ((FirstOrDefaultFunction)fn).info;
                    var c = EvalPointer(info.collectionPointer, scope, ctx);
                    var inner = info.function;
                    bool isList = c is object?[];
                    bool found = false;
                    object? foundValue = null;
                    IterateCollection(c, ctx, (entry, key, _) =>
                    {
                        if (found) return;
                        if (inner is null) { found = true; foundValue = entry; return; }
                        var innerScope = PushParams(scope, inner.parameters,
                            keyOrIndex: key, entry: entry, isList: isList);
                        NeoScriptExecutionResult result = ExecuteCollectionCallback(
                            inner,
                            innerScope,
                            ctx);
                        if (result.Returned && result.ReturnValue is bool b && b)
                        {
                            found = true;
                            foundValue = entry;
                        }
                    });
                    if (found) return foundValue;
                    if (isFirst)
                    {
                        throw new NSGetterRuntimeError(
                            inner is null
                                ? "First() called on an empty collection"
                                : "First() found no matching entry");
                    }
                    return null;
                }
                case SelectFunction sf:
                {
                    var c = EvalPointer(sf.info.collectionPointer, scope, ctx);
                    var inner = sf.info.function;
                    bool isList = c is object?[];
                    var acc = new List<object?>();
                    IterateCollection(c, ctx, (entry, key, _) =>
                    {
                        var innerScope = PushParams(scope, inner.parameters,
                            keyOrIndex: key, entry: entry, isList: isList);
                        NeoScriptExecutionResult result = ExecuteCollectionCallback(
                            inner,
                            innerScope,
                            ctx);
                        if (result.Returned)
                        {
                            ctx.allocationTracker
                                .ConsumeProducedCollectionEntry();
                            acc.Add(result.ReturnValue);
                        }
                    });
                    return acc.ToArray();
                }
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown function kind {fn.GetType().Name}");
            }
        }

        /// <summary>
        /// Collection callbacks are ordinary synchronous NeoScript frames.
        /// They inherit the ambient context, allocation tracker, write
        /// targets, and call-cycle state; only deferred suspension remains
        /// illegal. Keeping this seam on the shared executor prevents
        /// Where/First/FirstOrDefault/Select from silently falling back to
        /// the legacy read-only getter instruction walker.
        /// </summary>
        private static NeoScriptExecutionResult ExecuteCollectionCallback(
            FunctionWithReturnType callback,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                ctx.client,
                callback,
                scope,
                ctx);
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "collection callback cannot suspend");
                throw new NSGetterRuntimeError(
                    "Collection callbacks cannot call deferred Functions.");
            }
            return result;
        }

        private static object? EvalListIndex(
            FunctionListIndexInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            object? collection = EvalPointer(info.collectionPointer, scope, ctx);
            if (collection is null)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot use List index '{info.schemaKey}' on null");
            }
            collection = UnwrapGeneratedValue(collection, ctx);
            if (collection is not object?[])
            {
                throw new NSGetterRuntimeError(
                    $"List index '{info.schemaKey}' receiver is not a schema-backed List");
            }
            if (!TryFindRowReferenceByReference(collection, ctx, out RowReference row))
            {
                throw new NSGetterRuntimeError(
                    $"List index '{info.schemaKey}' receiver has no backing value row");
            }
            if (!ctx.client.TryGetMember(info.listMemberId, out ListMember? listMember))
            {
                throw new NSGetterRuntimeError(
                    $"List index IR references missing List member '{info.listMemberId}'");
            }

            NeoMemberList listNode;
            if (ctx.client.TryGetNode(
                    info.listMemberId,
                    row.valueId,
                    row.ownership,
                    out NeoMember? existing)
                && existing is NeoMemberList existingList)
            {
                listNode = existingList;
            }
            else
            {
                NeoMember created = row.ownership == NeoValueOwnership.Asset
                    ? NeoMember.Create(ctx.client, listMember, row.valueId)
                    : NeoMember.CreateWritable(
                        ctx.client,
                        listMember,
                        row.valueId,
                        row.ownership);
                if (created is not NeoMemberList createdList)
                {
                    throw new NSGetterRuntimeError(
                        $"List index '{info.schemaKey}' could not materialize its runtime List node");
                }
                listNode = createdList;
            }

            if (info.keyKind != ListIndexKeyKind.String
                && info.keyKind != ListIndexKeyKind.Enum)
            {
                throw new NSGetterRuntimeError(
                    $"List index '{info.schemaKey}' has unknown key kind '{info.keyKind}'");
            }

            try
            {
                NeoRawListIndex index = listNode.GetDerivedIndex(info.schemaKey, info.unique);
                index.ValidateKeyContract(info.keyKind, info.keyEnumId);
                if (info.keyPointer is null)
                {
                    var view = new Dictionary<string, object?>();
                    foreach (string indexedKey in index.Keys)
                    {
                        if (info.unique)
                        {
                            if (index.TryGetUnique(indexedKey, out string? valueId))
                            {
                                view[indexedKey] = ResolveValueIfId(
                                    valueId,
                                    ctx,
                                    row.ownership);
                            }
                            continue;
                        }
                        IReadOnlyList<string> bucket = index.GetMany(indexedKey);
                        var bucketIds = new object?[bucket.Count];
                        for (int i = 0; i < bucket.Count; i++)
                        {
                            bucketIds[i] = ResolveValueIfId(
                                bucket[i],
                                ctx,
                                row.ownership);
                        }
                        view[indexedKey] = bucketIds;
                    }
                    return view;
                }

                object? key = EvalPointer(info.keyPointer, scope, ctx);
                if (key is not string rawKey)
                {
                    throw new NSGetterRuntimeError(
                        $"List index '{info.schemaKey}' key must be a String or Enum option id");
                }
                if (info.unique)
                {
                    if (!index.TryGetUnique(rawKey, out string? valueId)) return null;
                    return ResolveValueIfId(valueId, ctx, row.ownership);
                }
                IReadOnlyList<string> valueIds = index.GetMany(rawKey);
                var result = new object?[valueIds.Count];
                for (int i = 0; i < valueIds.Count; i++) result[i] = valueIds[i];
                return result;
            }
            catch (InvalidOperationException error)
            {
                throw new NSGetterRuntimeError(
                    $"List index '{info.schemaKey}' failed: {error.Message}");
            }
            catch (KeyNotFoundException error)
            {
                throw new NSGetterRuntimeError(
                    $"List index '{info.schemaKey}' is stale: {error.Message}");
            }
        }

        private static int CollectionLength(object? c)
        {
            if (c is object?[] arr) return arr.Length;
            if (c is IDictionary<string, object?> dict) return dict.Count;
            if (c is string s) return s.Length;
            throw new NSGetterRuntimeError(
                $"Cannot Count() {ReceiverTypeName(c)}; expected list, dictionary, or string");
        }

        private static object EvalVectorConstructor(
            FunctionVectorConstructorInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var components = new float[info.componentPointers.Length];
            for (int i = 0; i < info.componentPointers.Length; i++)
            {
                var raw = EvalPointer(info.componentPointers[i], scope, ctx);
                if (!TryAsDouble(raw, out double numeric)
                    || double.IsNaN(numeric)
                    || double.IsInfinity(numeric))
                {
                    throw new NSGetterRuntimeError(
                        $"{info.vectorType} component must be numeric; got {ReceiverTypeName(raw)}.");
                }
                components[i] = (float)numeric;
            }

            switch (info.vectorType)
            {
                case MemberKind.Vector2:
                    EnsureVectorArity(components, 2, info.vectorType);
                    return new NeoVector2Value { x = components[0], y = components[1] };
                case MemberKind.Vector2Int:
                    EnsureVectorArity(components, 2, info.vectorType);
                    RequireIntegerComponent(components[0], "x");
                    RequireIntegerComponent(components[1], "y");
                    return new NeoVector2Value { x = components[0], y = components[1] };
                case MemberKind.Vector3:
                    EnsureVectorArity(components, 3, info.vectorType);
                    return new NeoVector3Value { x = components[0], y = components[1], z = components[2] };
                case MemberKind.Vector3Int:
                    EnsureVectorArity(components, 3, info.vectorType);
                    RequireIntegerComponent(components[0], "x");
                    RequireIntegerComponent(components[1], "y");
                    RequireIntegerComponent(components[2], "z");
                    return new NeoVector3Value { x = components[0], y = components[1], z = components[2] };
                default:
                    throw new NSGetterRuntimeError($"Unsupported vector constructor '{info.vectorType}'.");
            }
        }

        /// <summary>
        /// P42 §2.3. Evaluates the <c>imageSlice</c> intrinsic —
        /// <c>Images.&lt;Name&gt;.Slice(n)</c> — into the same
        /// <c>{ fileId, sliceIndex }</c> record a <see cref="SpriteMemberValue"/>
        /// unwraps to, so the produced value is interchangeable with a stored
        /// sprite everywhere downstream.
        ///
        /// <para>The registry symbol was already resolved to the project file
        /// record id by the compiler, against the project document; the file
        /// half therefore arrives here as a plain string and this evaluator
        /// looks nothing up. <see cref="NeoAssetDatabase"/> enters later, on
        /// the ordinary <c>NeoMemberSprite.Resolve()</c> path, once the record
        /// has been assigned to a sprite member — which is the one place the
        /// two runtimes resolve from different sources and is why
        /// <c>neoscript-registry-parity-fixture.json</c> exists. Validation
        /// order and message text are shared verbatim with the TS
        /// <c>evalImageSlice</c>.</para>
        /// </summary>
        private static object EvalImageSlice(
            FunctionImageSliceInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var fileId = EvalPointer(info.filePointer, scope, ctx) as string;
            if (string.IsNullOrEmpty(fileId))
            {
                throw new NSGetterRuntimeError(
                    "Slice(index) requires a project image reference.");
            }
            var rawSliceIndex = EvalPointer(info.sliceIndexPointer, scope, ctx);
            if (!TryAsDouble(rawSliceIndex, out double sliceIndex)
                || double.IsNaN(sliceIndex)
                || double.IsInfinity(sliceIndex))
            {
                throw new NSGetterRuntimeError(
                    $"Slice index must be numeric; got {ReceiverTypeName(rawSliceIndex)}.");
            }
            if (sliceIndex != System.Math.Truncate(sliceIndex))
            {
                throw new NSGetterRuntimeError("Slice index must be a whole number.");
            }
            if (sliceIndex < 0)
            {
                throw new NSGetterRuntimeError("Slice index must be 0 or greater.");
            }
            return new Dictionary<string, object?>
            {
                ["fileId"] = fileId,
                ["sliceIndex"] = (int)sliceIndex,
            };
        }

        /// <summary>
        /// Evaluates a <c>decimalOp</c> builtin
        /// (<c>Round</c>/<c>Divide</c>/<c>ToFloat</c>/<c>ToDecimal</c> —
        /// specs/decimal-member.md decision 7), mirroring the TS
        /// evaluator's <c>NSFunctionType.decimalOp</c> case. All decimal
        /// math flows through <see cref="NeoDecimalMath"/>; its distinct
        /// failure exceptions map onto <see cref="NSGetterRuntimeError"/>
        /// with their messages preserved.
        /// </summary>
        private static object? EvalDecimalOp(
            FunctionDecimalOpInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var receiverRaw = EvalPointer(info.receiverPointer, scope, ctx);
            if (receiverRaw is null)
            {
                throw new NSGetterRuntimeError($"Decimal {info.op} receiver is null.");
            }
            try
            {
                switch (info.op)
                {
                    case DecimalOpKind.Round:
                        return NeoDecimalMath.Round(
                            CoerceDecimalOperand(receiverRaw, "Round"),
                            EvalDecimalOpDigits(info, scope, ctx));
                    case DecimalOpKind.Divide:
                    {
                        if (info.argPointer is null)
                        {
                            throw new NSGetterRuntimeError(
                                "Decimal Divide is missing its divisor pointer.");
                        }
                        var divisorRaw = EvalPointer(info.argPointer, scope, ctx);
                        if (divisorRaw is null)
                        {
                            throw new NSGetterRuntimeError("Decimal Divide divisor is null.");
                        }
                        return NeoDecimalMath.Divide(
                            CoerceDecimalOperand(receiverRaw, "Divide"),
                            CoerceDecimalOperand(divisorRaw, "Divide"),
                            EvalDecimalOpDigits(info, scope, ctx));
                    }
                    case DecimalOpKind.ToFloat:
                        return NeoDecimalMath.ToFloat(
                            CoerceDecimalOperand(receiverRaw, "ToFloat"));
                    case DecimalOpKind.ToDecimal:
                    {
                        if (!TryAsDouble(receiverRaw, out double floatValue))
                        {
                            throw new NSGetterRuntimeError(
                                $"ToDecimal receiver must be a float; got {ReceiverTypeName(receiverRaw)}.");
                        }
                        return NeoDecimalMath.FromFloat(
                            floatValue,
                            EvalDecimalOpDigits(info, scope, ctx));
                    }
                    default:
                        throw new NSGetterRuntimeError($"Unknown decimal op '{info.op}'.");
                }
            }
            catch (DecimalOverflowException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
            catch (DecimalDivisionByZeroException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
            catch (DecimalDigitsRangeException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
            catch (DecimalNonFiniteException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
        }

        /// <summary>
        /// Evaluates a decimalOp's <c>digitsPointer</c> to an integer digit
        /// count (mirror of the TS evaluator's lazy <c>digits()</c> helper —
        /// distinct errors for a missing pointer vs a non-integer value; the
        /// 0..28 range check lives in <see cref="NeoDecimalMath"/>).
        /// </summary>
        private static int EvalDecimalOpDigits(
            FunctionDecimalOpInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            if (info.digitsPointer is null)
            {
                throw new NSGetterRuntimeError(
                    $"Decimal {info.op} is missing its digits pointer.");
            }
            var value = EvalPointer(info.digitsPointer, scope, ctx);
            if (!TryAsDouble(value, out double number) || number != System.Math.Truncate(number))
            {
                throw new NSGetterRuntimeError(
                    $"Decimal {info.op} digits argument must be an integer; got {ReceiverTypeName(value)}.");
            }
            return (int)number;
        }

        /// <summary>
        /// Evaluates a <c>stringOp</c> builtin (<c>ToLower</c>/<c>ToUpper</c>/
        /// <c>Trim</c>/<c>StartsWith</c>/<c>EndsWith</c>), mirroring the TS
        /// evaluator's <c>NSFunctionType.stringOp</c> case. This closes a
        /// pre-existing parity gap: the IR kind existed web-side but the
        /// dotnet converter had no <c>stringOp</c> variant, so such a
        /// function failed to deserialize.
        /// </summary>
        private static object EvalStringOp(
            FunctionStringOpInfo info,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            var receiver = EvalPointer(info.receiverPointer, scope, ctx);
            if (receiver is not string receiverText)
            {
                throw new NSGetterRuntimeError(
                    $"string.{info.op} receiver must be a string");
            }
            switch (info.op)
            {
                case StringOpKind.ToLower:
                {
                    string result = receiverText.ToLowerInvariant();
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
                case StringOpKind.ToUpper:
                {
                    string result = receiverText.ToUpperInvariant();
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
                case StringOpKind.Trim:
                {
                    string result = receiverText.Trim();
                    ctx.allocationTracker.ConsumeProducedStringCharacters(
                        result.Length);
                    return result;
                }
                case StringOpKind.StartsWith:
                case StringOpKind.EndsWith:
                {
                    if (info.argPointer is null)
                    {
                        throw new NSGetterRuntimeError(
                            $"string.{info.op} requires an argument");
                    }
                    var arg = EvalPointer(info.argPointer, scope, ctx);
                    if (arg is not string argText)
                    {
                        throw new NSGetterRuntimeError(
                            $"string.{info.op} argument must be a string");
                    }
                    return info.op == StringOpKind.StartsWith
                        ? receiverText.StartsWith(argText, StringComparison.Ordinal)
                        : receiverText.EndsWith(argText, StringComparison.Ordinal);
                }
                default:
                    throw new NSGetterRuntimeError($"Unknown string op {info.op}");
            }
        }

        private static void EnsureVectorArity(
            float[] components,
            int expected,
            MemberKind vectorType)
        {
            if (components.Length != expected)
            {
                throw new NSGetterRuntimeError(
                    $"{vectorType} takes {expected} numeric arguments, got {components.Length}.");
            }
        }

        private static void RequireIntegerComponent(float value, string component)
        {
            if (System.Math.Truncate(value) != value)
            {
                throw new NSGetterRuntimeError(
                    $"Vector component '{component}' must be an integer.");
            }
        }

        private readonly struct OrderedRawCollectionEntry
        {
            internal OrderedRawCollectionEntry(object? raw, object key)
            {
                Raw = raw;
                Key = key;
            }

            internal object? Raw { get; }
            internal object Key { get; }
        }

        /// <summary>
        /// One ordered raw membership retained at <c>foreach</c> entry. A
        /// removed Save/Session child row is retained by reference so the
        /// original entry can still be resolved later in the invocation;
        /// this is a membership snapshot, never a deep value clone.
        /// </summary>
        internal sealed class CollectionEntrySnapshot
        {
            private readonly object? raw;
            private readonly NeoValueOwnership? ownership;
            private readonly MemberValue? retainedRow;

            internal CollectionEntrySnapshot(
                object? raw,
                NeoValueOwnership? ownership,
                MemberValue? retainedRow)
            {
                this.raw = raw;
                this.ownership = ownership;
                this.retainedRow = retainedRow;
            }

            internal object? Resolve(Context ctx)
            {
                if (raw is not string id)
                {
                    return raw;
                }
                NeoValueOwnership resolvedOwnership = ownership
                    ?? ResolveOwnershipForValueId(ctx, id);
                bool hasExactCurrentRow = resolvedOwnership == NeoValueOwnership.Asset
                    || ctx.client.HasWritableValue(resolvedOwnership, id);
                if (hasExactCurrentRow
                    && ctx.client.TryGetValue(
                        resolvedOwnership,
                        id,
                        out MemberValue? currentRow))
                {
                    return UnwrapCached(currentRow, ctx, resolvedOwnership);
                }
                return retainedRow is null
                    ? raw
                    : UnwrapCached(retainedRow, ctx, resolvedOwnership);
            }
        }

        private static IEnumerable<OrderedRawCollectionEntry>
            OrderedRawCollectionEntries(object? collection)
        {
            if (collection is object?[] array)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    yield return new OrderedRawCollectionEntry(array[i], i);
                }
                yield break;
            }
            if (collection is IDictionary<string, object?> dictionary)
            {
                // ECMAScript Object.keys/Object.values order: canonical array
                // indices first in ascending numeric order, then every other
                // string key in insertion order. Newtonsoft preserves textual
                // object insertion order in Dictionary, but JavaScript has
                // already canonicalized its integer-index keys by the time the
                // web evaluator sees Object.values, so .NET must do the same.
                var indexed = new SortedDictionary<uint, OrderedRawCollectionEntry>();
                var strings = new List<OrderedRawCollectionEntry>();
                foreach (var pair in dictionary)
                {
                    var entry = new OrderedRawCollectionEntry(
                        pair.Value,
                        pair.Key);
                    if (TryGetEcmaArrayIndex(pair.Key, out uint index))
                    {
                        indexed[index] = entry;
                    }
                    else
                    {
                        strings.Add(entry);
                    }
                }
                foreach (OrderedRawCollectionEntry entry in indexed.Values)
                {
                    yield return entry;
                }
                foreach (OrderedRawCollectionEntry entry in strings)
                {
                    yield return entry;
                }
            }
        }

        private static bool TryGetEcmaArrayIndex(string key, out uint index)
        {
            if (!uint.TryParse(
                    key,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out index)
                || index == uint.MaxValue)
            {
                return false;
            }
            return key == index.ToString(CultureInfo.InvariantCulture);
        }

        internal static CollectionEntrySnapshot[] SnapshotCollectionEntries(
            object? collection,
            Context ctx)
        {
            if (collection is not object?[]
                && collection is not IDictionary<string, object?>)
            {
                throw new NSGetterRuntimeError(
                    "foreach receiver must be a List, Dictionary, Set/Lookup, or derived collection view.");
            }

            var snapshot = new List<CollectionEntrySnapshot>();
            foreach (OrderedRawCollectionEntry entry in
                OrderedRawCollectionEntries(collection))
            {
                ctx.allocationTracker.ConsumeCollectionVisit();
                MemberValue? retainedRow = null;
                NeoValueOwnership? entryOwnership = null;
                if (entry.Raw is string id)
                {
                    NeoValueOwnership resolvedOwnership =
                        ResolveOwnershipForValueId(ctx, id);
                    entryOwnership = resolvedOwnership;
                    ctx.client.TryGetValue(
                        resolvedOwnership,
                        id,
                        out retainedRow);
                }
                snapshot.Add(new CollectionEntrySnapshot(
                    entry.Raw,
                    entryOwnership,
                    retainedRow));
            }
            return snapshot.ToArray();
        }

        private static IEnumerable<object?> CollectionEntries(object? c, Context ctx)
        {
            foreach (OrderedRawCollectionEntry entry in OrderedRawCollectionEntries(c))
            {
                ctx.allocationTracker.ConsumeCollectionVisit();
                yield return ResolveValueIfId(entry.Raw, ctx);
            }
        }

        private static void IterateCollection(
            object? c,
            Context ctx,
            Action<object? /*entry*/, object /*key*/, string? /*valueId*/> callback)
        {
            foreach (OrderedRawCollectionEntry rawEntry in
                OrderedRawCollectionEntries(c))
            {
                ctx.allocationTracker.ConsumeCollectionVisit();
                object? entry = ResolveValueIfId(
                    rawEntry.Raw,
                    ctx);
                callback(
                    entry,
                    rawEntry.Key,
                    rawEntry.Raw as string);
            }
        }

        private static Dictionary<string, object?> PushParams(
            Dictionary<string, object?> parent,
            Variable[] parameters,
            object keyOrIndex,
            object? entry,
            bool isList)
        {
            var child = new Dictionary<string, object?>(parent);
            if (parameters.Length == 1)
            {
                child[parameters[0].id] = entry;
            }
            else if (parameters.Length == 2)
            {
                object first = isList
                    ? (object)System.Convert.ToInt32(keyOrIndex, CultureInfo.InvariantCulture)
                    : keyOrIndex.ToString()!;
                child[parameters[0].id] = first;
                child[parameters[1].id] = entry;
            }
            return child;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static bool JsTruthy(object? value)
        {
            if (value is null) return false;
            if (value is bool b) return b;
            if (TryAsDouble(value, out double d)) return d != 0 && !double.IsNaN(d);
            if (value is string s) return s.Length > 0;
            return true;
        }

        private static bool JsEqual(object? a, object? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            // Numeric-tolerant equality (int vs double both come through as numbers).
            if (TryAsDouble(a, out double da) && TryAsDouble(b, out double db)) return da == db;
            if (a is string sa && b is string sb) return sa == sb;
            if (a is bool ba && b is bool bb) return ba == bb;
            if (a is object?[] aa && b is object?[] ab)
            {
                if (aa.Length != ab.Length) return false;
                for (int i = 0; i < aa.Length; i++)
                {
                    if (!JsEqual(aa[i], ab[i])) return false;
                }
                return true;
            }
            if (a is IDictionary<string, object?> ad && b is IDictionary<string, object?> bd)
            {
                if (ad.Count != bd.Count) return false;
                foreach (var kvp in ad)
                {
                    if (!bd.TryGetValue(kvp.Key, out var bv)) return false;
                    if (!JsEqual(kvp.Value, bv)) return false;
                }
                return true;
            }
            return Equals(a, b);
        }

        private static double NumericCompare(object? a, object? b)
        {
            if (!TryAsDouble(a, out double da) || !TryAsDouble(b, out double db))
            {
                throw new NSGetterRuntimeError(
                    $"Comparison requires numeric operands; got {ReceiverTypeName(a)} and {ReceiverTypeName(b)}");
            }
            return da - db;
        }

        /// <summary>
        /// Runtime tag-check for <c>is</c>. Mirrors TS-side
        /// <c>runtimeTypeCheck</c>.
        /// </summary>
        private static bool RuntimeTypeCheck(object? value, TypeInfo checkType, Context ctx)
        {
            if (checkType.type == MemberKind.Null) return value is null;
            if (value is null) return false;
            switch (checkType.type)
            {
                case MemberKind.Bool: return value is bool;
                case MemberKind.Int:
                    return TryAsDouble(value, out double di) && di == System.Math.Truncate(di);
                case MemberKind.Float: return TryAsDouble(value, out _);
                case MemberKind.String: return value is string;
                case MemberKind.Sprite:
                    return value is IDictionary<string, object?> sprite &&
                        sprite.TryGetValue("fileId", out var spriteFileId) &&
                        spriteFileId is string &&
                        sprite.TryGetValue("sliceIndex", out var sliceIndex) &&
                        TryAsDouble(sliceIndex, out double slice) &&
                        slice == System.Math.Truncate(slice);
                case MemberKind.Audio:
                    return value is IDictionary<string, object?> audio &&
                        audio.TryGetValue("fileId", out var audioFileId) &&
                        audioFileId is string;
                case MemberKind.Vector2:
                    return IsVector2Value(value, requireIntegers: false);
                case MemberKind.Vector2Int:
                    return IsVector2Value(value, requireIntegers: true);
                case MemberKind.Vector3:
                    return IsVector3Value(value, requireIntegers: false);
                case MemberKind.Vector3Int:
                    return IsVector3Value(value, requireIntegers: true);
                case MemberKind.Color:
                    return IsColorValue(value);
                case MemberKind.Decimal:
                    // Decimal runtime values ARE canonical decimal strings
                    // (specs/decimal-member.md decision 7).
                    return value is string decimalText
                        && NeoDecimalValues.GetViolation(decimalText) == NeoDecimalValues.Violation.None;
                case MemberKind.List: return value is object?[];
                case MemberKind.Dictionary:
                    return value is IDictionary<string, object?>;
                case MemberKind.Enum:
                {
                    if (value is not object?[] arr) return false;
                    foreach (var e in arr) if (e is not string) return false;
                    return true;
                }
                case MemberKind.Class:
                {
                    if (value is not IDictionary<string, object?>) return false;
                    string? runtimeClassId = FindRowClassIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(runtimeClassId)) return false;
                    string checkClassId = (checkType as ClassTypeInfo)?.classId ?? "";
                    if (runtimeClassId == checkClassId) return true;
                    try
                    {
                        var chain = ctx.client.ResolveClassInheritanceChain(runtimeClassId!);
                        foreach (var t in chain) if (t.id == checkClassId) return true;
                    }
                    catch (CircularInheritanceError)
                    {
                        return false;
                    }
                    return false;
                }
                case MemberKind.Interface:
                {
                    if (value is not IDictionary<string, object?>) return false;
                    string? runtimeClassId = FindRowClassIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(runtimeClassId)) return false;
                    string interfaceId = (checkType as InterfaceTypeInfo)?.interfaceId ?? "";
                    if (string.IsNullOrEmpty(interfaceId)) return false;
                    return NeoInterfaceResolution.ClassImplements(
                        runtimeClassId!,
                        interfaceId,
                        ctx.client.ProjectDataForRuntime);
                }
                default: return false;
            }
        }

        /// <summary>
        /// If <paramref name="at"/> is a string id that resolves to a row,
        /// returns the row's value. Single-select Lookup arrays
        /// (<c>string[]</c> of length 1) get one extra unwrap so
        /// <c>this.equipped.name</c> works on a single-select Lookup.
        /// Routes through <see cref="UnwrapCached"/> so the same heap
        /// object round-trips for the same id within a Compute call.
        /// </summary>
        private static object? ResolveValueIfId(
            object? at,
            Context ctx,
            NeoValueOwnership? preferredOwnership = null,
            JsonMember? member = null)
        {
            if (at is not string id) return at;
            var ownership = preferredOwnership ?? ResolveOwnershipForValueId(ctx, id);
            if (!ctx.client.TryGetValue(ownership, id, out MemberValue? row)) return at;
            var v = UnwrapCached(row, ctx, ownership, member);
            if (member is LookupMember
                && v is object?[] arr
                && arr.Length == 1
                && arr[0] is string singleId)
            {
                var singleOwnership = ResolveOwnershipForValueId(ctx, singleId);
                if (ctx.client.TryGetValue(singleOwnership, singleId, out MemberValue? next))
                {
                    return UnwrapCached(next, ctx, singleOwnership);
                }
            }
            return v;
        }

        private static object? UnwrapGeneratedValue(object? value, Context ctx)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                var ownership = ResolveOwnershipForValueId(ctx, reference.valueId!);
                if (ctx.client.TryGetValue(
                        ownership,
                        reference.valueId!,
                        out MemberValue? row))
                {
                    return UnwrapCached(row, ctx, ownership);
                }
            }
            return value;
        }

        private static NeoValueOwnership ResolveOwnershipForValueId(
            Context ctx,
            string valueId)
        {
            return ctx.client.TryGetValueOwnership(valueId, out NeoValueOwnership ownership)
                ? ownership
                : ctx.valueOwnership;
        }

        private static string? ValueIdOf(object? value, Context ctx)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                return reference.valueId;
            }
            return FindRowIdByReference(value, ctx);
        }

        /// <summary>
        /// Resolves an evaluator-shaped value back to the Neo row it came
        /// from. Internal so the shared construction path in
        /// <see cref="NeoGeneratedTypesSupport"/> can attach an initializer's
        /// product the same way a constructor argument is attached.
        /// </summary>
        internal static NeoConstructorValueReference?
            ConstructorReferenceOf(object? value, Context ctx)
        {
            string? valueId = ValueIdOf(value, ctx);
            if (string.IsNullOrEmpty(valueId)) return null;
            NeoValueOwnership? ownership = value is NeoGeneratedClassValue generated
                ? generated.ValueOwnership
                : FindRowOwnershipByReference(value, ctx);
            return new NeoConstructorValueReference(valueId!, ownership);
        }

        // ---------------------------------------------------------------
        // Wire-value bridging — turns MemberValue subclasses into the
        // plain CLR shapes the evaluator manipulates (object?[],
        // IDictionary, primitives, etc.).
        // ---------------------------------------------------------------

        private static object? ExtractWireValue(
            MemberValue row,
            NeoValueOwnership ownership,
            JsonMember? member,
            Context ctx)
        {
            return row switch
            {
                BoolMemberValue b => b.value,
                NumberMemberValue n => n.value,
                // A Decimal member's row is a StringMemberValue
                // (specs/decimal-member.md decision 5) whose schema
                // member is a DecimalMember — it falls to the raw
                // `s.value` arm here, which is exactly right: the evaluator's
                // decimal representation IS the canonical stored string, and
                // localization never applies. No Decimal case is needed.
                StringMemberValue s => member is StringMember stringMember
                    ? ResolveStringValue(s, stringMember, ctx)
                    : s.value,
                ArrayMemberValue a => a.value is null
                    ? null
                    : ToObjectArray(a.value),
                ObjectMemberValue o => o.value is null
                    ? null
                    : ToObjectDict(row.id, ownership, o.value),
                DelegateMemberValue d => d.value,
                FileMemberValue f => f.value is null
                    ? null
                    : new Dictionary<string, object?> { ["fileId"] = f.value.fileId },
                SpriteMemberValue sp => sp.value is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["fileId"] = sp.value.fileId,
                        ["sliceIndex"] = sp.value.sliceIndex,
                    },
                Vector2MemberValue v => v.value,
                Vector3MemberValue v => v.value,
                ColorMemberValue c => c.value,
                NullMemberValue _ => null,
                _ => null,
            };
        }

        /// <summary>
        /// Unwraps a row through the per-context cache so the same
        /// heap object round-trips across calls for the same id —
        /// matching the TS evaluator's "JS row.value is the heap
        /// object" reference-stability. First call materialises +
        /// caches; subsequent calls return the cached instance.
        ///
        /// <para>For object-shaped values (records, arrays) also
        /// populates the reverse index so
        /// <see cref="FindRowIdByReference"/> /
        /// <see cref="FindRowClassIdByReference"/> can recover the
        /// source row from the unwrapped value. Skipped for
        /// primitives because boxed-primitive reference equality
        /// would false-positive across rows that share a value.</para>
        /// </summary>
        private static object? UnwrapCached(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership,
            JsonMember? member = null)
        {
            string cacheKey = RowCacheKey(ownership, row.id, member);
            if (ctx.rowUnwrapCache.TryGetValue(cacheKey, out var cached)) return cached;
            var unwrapped = ExtractWireValue(row, ownership, member, ctx);
            ctx.rowUnwrapCache[cacheKey] = unwrapped;
            string rowCacheKey = RowCacheRowKey(ownership, row.id);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    rowCacheKey, out HashSet<string>? rowKeys))
            {
                rowKeys = new HashSet<string>();
                ctx.rowCacheKeysByRow[rowCacheKey] = rowKeys;
            }
            rowKeys.Add(cacheKey);
            // Reverse-index only object-shaped unwraps. Primitive
            // boxes don't have meaningful reference identity for our
            // lookups (two rows with `value = "hi"` would share a
            // boxed string; two rows with `value = 5` would share a
            // boxed double after JIT folding). The TS reference-
            // equality lookup only ever fires for record / array
            // values where this is a non-issue.
            //
            // P42 §3: `NeoVector2Value` (and its `NeoVector3Value`
            // subclass) and `NeoColorValue` join that set. They are
            // reference types materialised per row, so they have exactly
            // the identity records and arrays have — and on the TS side a
            // vector/colour row's `.value` IS the plain record the
            // reference lookup already finds. Without this, a structured
            // leaf receiver cannot be traced back to its row and
            // `Foo.Position.y = 0.25` dies at "Assignment receiver is not
            // backed by a Neo value row." A sprite row needed nothing: it
            // already unwraps to an `IDictionary`.
            if (unwrapped is IDictionary<string, object?>
                || unwrapped is object?[]
                || unwrapped is NeoVector2Value
                || unwrapped is NeoColorValue)
            {
                string? effectiveClassId = row.classId
                    ?? (member as ClassMember)?.classId;
                ctx.rowReverseIndex[unwrapped!] = new RowReference(
                    row.id,
                    ownership,
                    effectiveClassId);
            }
            return unwrapped;
        }

        /// <summary>
        /// Keeps the per-evaluation row cache coherent after a mutation-capable
        /// NeoScript frame writes a row. Object-shaped rows are patched in
        /// place so an already-bound <c>this</c> / nested Class receiver keeps
        /// both its identity and its updated child ids. Fixed-size arrays are
        /// patched when possible; values whose CLR shape cannot be updated in
        /// place are evicted so the next read materialises the new row value.
        /// </summary>
        internal static void RefreshCachedRowAfterWrite(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership)
        {
            string rowCacheKey = RowCacheRowKey(ownership, row.id);
            ctx.rowCacheKeysByRow.TryGetValue(
                rowCacheKey,
                out HashSet<string>? indexedKeys);
            var matchingKeys = indexedKeys is null
                ? new List<string>()
                : new List<string>(indexedKeys);
            var patchedObjects = new HashSet<object>(
                ReferenceEqualityComparer.Instance);

            foreach (string key in matchingKeys)
            {
                if (!ctx.rowUnwrapCache.TryGetValue(key, out object? cached))
                {
                    indexedKeys!.Remove(key);
                    continue;
                }
                if (PatchCachedShape(row, cached))
                {
                    if (cached is not null) patchedObjects.Add(cached);
                    continue;
                }

                ctx.rowUnwrapCache.Remove(key);
                indexedKeys!.Remove(key);
                // Keep reverse provenance for existing locals/arguments even
                // when a fixed-size CLR shape (notably object[]) cannot be
                // patched in place. A future row read materializes a fresh
                // canonical shape, while the old alias can still resolve and
                // write through its authoritative backing row.
            }
            if (indexedKeys is not null && indexedKeys.Count == 0)
            {
                ctx.rowCacheKeysByRow.Remove(rowCacheKey);
            }

            // A Session constructor graph can be promoted into Save while a
            // local/argument still aliases one of its CLR objects. The
            // canonical unwrap cache has one entry per row, but all existing
            // aliases remain in the reverse index. Patch those aliases too so
            // subsequent reads observe writes through the promoted row.
            foreach (var pair in ctx.rowReverseIndex.ToArray())
            {
                if (pair.Value.valueId != row.id
                    || pair.Value.ownership != ownership
                    || patchedObjects.Contains(pair.Key))
                {
                    continue;
                }
                PatchCachedShape(row, pair.Key);
            }
        }

        private static bool PatchCachedShape(MemberValue row, object? cached)
        {
            if (row is ObjectMemberValue objectRow
                && cached is IDictionary<string, object?> record)
            {
                record.Clear();
                if (objectRow.value is not null)
                {
                    foreach (var pair in objectRow.value)
                    {
                        record[pair.Key] = pair.Value;
                    }
                }
                return true;
            }
            if (row is ArrayMemberValue arrayRow
                && cached is object?[] array
                && arrayRow.value is not null
                && array.Length == arrayRow.value.Length)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = arrayRow.value[i];
                }
                return true;
            }
            if (row is FileMemberValue fileRow
                && cached is IDictionary<string, object?> fileRecord)
            {
                fileRecord.Clear();
                if (fileRow.value is not null)
                {
                    fileRecord["fileId"] = fileRow.value.fileId;
                }
                return true;
            }
            if (row is SpriteMemberValue spriteRow
                && cached is IDictionary<string, object?> spriteRecord)
            {
                spriteRecord.Clear();
                if (spriteRow.value is not null)
                {
                    spriteRecord["fileId"] = spriteRow.value.fileId;
                    spriteRecord["sliceIndex"] = spriteRow.value.sliceIndex;
                }
                return true;
            }
            // P42 §3. Vector and colour unwraps are the row's own payload
            // object, so a clone-on-write shadow leaves existing aliases
            // pointing at the pre-write instance. Patch them in place for the
            // same reason the record arms above do — otherwise a NeoScript
            // field write is invisible to a `this` receiver already bound in
            // the current frame. Vector3 is checked before Vector2 because
            // `NeoVector3Value` derives from `NeoVector2Value`.
            if (row is Vector3MemberValue vector3Row
                && cached is NeoVector3Value vector3Cached)
            {
                NeoVector3Value? vector3Next = vector3Row.value;
                if (ReferenceEquals(vector3Next, vector3Cached)) return true;
                if (vector3Next is null) return false;
                vector3Cached.x = vector3Next.x;
                vector3Cached.y = vector3Next.y;
                vector3Cached.z = vector3Next.z;
                return true;
            }
            if (row is Vector2MemberValue vector2Row
                && cached is NeoVector2Value vector2Cached
                && cached is not NeoVector3Value)
            {
                NeoVector2Value? vector2Next = vector2Row.value;
                if (ReferenceEquals(vector2Next, vector2Cached)) return true;
                if (vector2Next is null) return false;
                vector2Cached.x = vector2Next.x;
                vector2Cached.y = vector2Next.y;
                return true;
            }
            if (row is ColorMemberValue colorRow
                && cached is NeoColorValue colorCached)
            {
                NeoColorValue? colorNext = colorRow.value;
                if (ReferenceEquals(colorNext, colorCached)) return true;
                if (colorNext is null) return false;
                colorCached.r = colorNext.r;
                colorCached.g = colorNext.g;
                colorCached.b = colorNext.b;
                colorCached.a = colorNext.a;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retargets every cached CLR alias whose row was atomically moved
        /// from one writable store to another. Row ids remain stable during a
        /// Session-to-Save promotion; only provenance changes.
        /// </summary>
        internal static void RetargetCachedRowsAfterMove(
            Context ctx,
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership)
        {
            if (sourceOwnership == targetOwnership) return;
            var movedRowIds = new HashSet<string>();
            foreach (var pair in ctx.rowReverseIndex.ToArray())
            {
                RowReference row = pair.Value;
                if (row.ownership != sourceOwnership
                    || ctx.client.HasWritableValue(sourceOwnership, row.valueId)
                    || !ctx.client.HasWritableValue(targetOwnership, row.valueId))
                {
                    continue;
                }
                ctx.rowReverseIndex[pair.Key] = new RowReference(
                    row.valueId,
                    targetOwnership,
                    row.classId);
                movedRowIds.Add(row.valueId);
            }

            string sourcePrefix = sourceOwnership + ":";
            foreach (string rowCacheKey in ctx.rowCacheKeysByRow.Keys.ToArray())
            {
                if (!rowCacheKey.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                string rowId = rowCacheKey.Substring(sourcePrefix.Length);
                if (ctx.client.HasWritableValue(sourceOwnership, rowId)
                    || !ctx.client.HasWritableValue(targetOwnership, rowId))
                {
                    continue;
                }
                movedRowIds.Add(rowId);
            }

            foreach (string rowId in movedRowIds)
            {
                RetargetCachedRow(
                    ctx,
                    sourceOwnership,
                    targetOwnership,
                    rowId);
            }
        }

        private static void RetargetCachedRow(
            Context ctx,
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership,
            string rowId)
        {
            string sourceRowKey = RowCacheRowKey(sourceOwnership, rowId);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    sourceRowKey,
                    out HashSet<string>? sourceKeys))
            {
                return;
            }
            string targetRowKey = RowCacheRowKey(targetOwnership, rowId);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    targetRowKey,
                    out HashSet<string>? targetKeys))
            {
                targetKeys = new HashSet<string>();
                ctx.rowCacheKeysByRow[targetRowKey] = targetKeys;
            }
            string sourcePrefix = sourceOwnership + ":";
            string targetPrefix = targetOwnership + ":";
            foreach (string sourceKey in sourceKeys.ToArray())
            {
                string targetKey = targetPrefix + sourceKey.Substring(sourcePrefix.Length);
                if (ctx.rowUnwrapCache.TryGetValue(sourceKey, out object? cached))
                {
                    ctx.rowUnwrapCache.Remove(sourceKey);
                    // Prefer the moving graph's object so locals bound before
                    // promotion remain the canonical unwrap for future reads.
                    ctx.rowUnwrapCache[targetKey] = cached;
                }
                targetKeys.Add(targetKey);
            }
            ctx.rowCacheKeysByRow.Remove(sourceRowKey);
        }

        internal static void EvictCachedRows(
            Context ctx,
            NeoValueOwnership ownership,
            IEnumerable<string> rowIds)
        {
            var removed = new HashSet<string>(rowIds);
            foreach (string rowId in removed)
            {
                string rowKey = RowCacheRowKey(ownership, rowId);
                if (ctx.rowCacheKeysByRow.TryGetValue(
                        rowKey,
                        out HashSet<string>? cacheKeys))
                {
                    foreach (string cacheKey in cacheKeys)
                    {
                        ctx.rowUnwrapCache.Remove(cacheKey);
                    }
                    ctx.rowCacheKeysByRow.Remove(rowKey);
                }
            }
            foreach (var pair in ctx.rowReverseIndex.ToArray())
            {
                if (pair.Value.ownership == ownership
                    && removed.Contains(pair.Value.valueId))
                {
                    ctx.rowReverseIndex.Remove(pair.Key);
                }
            }
        }

        private static string RowCacheRowKey(
            NeoValueOwnership ownership,
            string rowId) =>
            ownership.ToString() + ":" + rowId;

        private static string RowCacheKey(
            NeoValueOwnership ownership,
            string rowId,
            JsonMember? member = null) =>
            ownership.ToString() + ":" + rowId + ":" + (member?.id ?? "");

        private static string? ResolveStringValue(
            StringMemberValue value,
            StringMember member,
            Context ctx)
        {
            if (value.value == null) return null;
            if (!member.localizable) return value.value;
            if (value.neoLocalizationMode == NeoStringLocalizationMode.Literal) return value.value;
            return ctx.client.Localization.ResolveText(value.value);
        }

        private static object?[] ToObjectArray(string[] arr)
        {
            var result = new object?[arr.Length];
            for (int i = 0; i < arr.Length; i++) result[i] = arr[i];
            return result;
        }

        private sealed class NeoObjectRecord
            : Dictionary<string, object?>, INeoValueReference
        {
            public string? valueId { get; }
            public NeoValueOwnership valueOwnership { get; }

            public NeoObjectRecord(string valueId, NeoValueOwnership ownership, int capacity)
                : base(capacity)
            {
                this.valueId = valueId;
                valueOwnership = ownership;
            }
        }

        private static IDictionary<string, object?> ToObjectDict(
            string rowId,
            NeoValueOwnership ownership,
            IDictionary<string, string> dict)
        {
            var result = new NeoObjectRecord(rowId, ownership, dict.Count);
            foreach (var kvp in dict) result[kvp.Key] = kvp.Value;
            return result;
        }

        private static object? UnwrapJToken(Newtonsoft.Json.Linq.JToken? token)
        {
            if (token is null) return null;
            switch (token.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.Null:
                case Newtonsoft.Json.Linq.JTokenType.Undefined:
                    return null;
                case Newtonsoft.Json.Linq.JTokenType.Boolean: return token.Value<bool>();
                case Newtonsoft.Json.Linq.JTokenType.Integer: return token.Value<double>();
                case Newtonsoft.Json.Linq.JTokenType.Float: return token.Value<double>();
                case Newtonsoft.Json.Linq.JTokenType.String: return token.Value<string>();
                case Newtonsoft.Json.Linq.JTokenType.Array:
                {
                    var arr = (Newtonsoft.Json.Linq.JArray)token;
                    var result = new object?[arr.Count];
                    for (int i = 0; i < arr.Count; i++) result[i] = UnwrapJToken(arr[i]);
                    return result;
                }
                case Newtonsoft.Json.Linq.JTokenType.Object:
                {
                    var obj = (Newtonsoft.Json.Linq.JObject)token;
                    var result = new Dictionary<string, object?>();
                    foreach (var kvp in obj) result[kvp.Key] = UnwrapJToken(kvp.Value);
                    return result;
                }
                default: return token.ToString();
            }
        }

        private static bool TryAsObjectRecord(
            object? value,
            out IDictionary<string, object?>? record)
        {
            if (value is IDictionary<string, object?> d)
            {
                record = d;
                return true;
            }
            record = null;
            return false;
        }

        private static bool TryAsDouble(object? value, out double result)
        {
            switch (value)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case int i: result = i; return true;
                case long l: result = l; return true;
                case short sh: result = sh; return true;
                case decimal dec: result = (double)dec; return true;
                default: result = 0; return false;
            }
        }

        private static int ToIntKey(object? key)
        {
            if (TryAsDouble(key, out double d) && d == System.Math.Truncate(d))
            {
                return (int)d;
            }
            if (key is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            {
                return i;
            }
            throw new NSGetterRuntimeError($"List index must be an integer; got '{key}'");
        }

        private static bool TryReadVectorComponent(
            object? value,
            string key,
            out float component)
        {
            component = 0;
            if (value is Vector2 vector2 && value is not Vector3)
            {
                if (key == "x") { component = vector2.x; return true; }
                if (key == "y") { component = vector2.y; return true; }
                return false;
            }
            if (value is Vector2Int vector2Int && value is not Vector3Int)
            {
                if (key == "x") { component = vector2Int.x; return true; }
                if (key == "y") { component = vector2Int.y; return true; }
                return false;
            }
            if (value is Vector3 vector3)
            {
                if (key == "x") { component = vector3.x; return true; }
                if (key == "y") { component = vector3.y; return true; }
                if (key == "z") { component = vector3.z; return true; }
                return false;
            }
            if (value is Vector3Int vector3Int)
            {
                if (key == "x") { component = vector3Int.x; return true; }
                if (key == "y") { component = vector3Int.y; return true; }
                if (key == "z") { component = vector3Int.z; return true; }
                return false;
            }
            if (value is NeoVector2Value v2)
            {
                if (key == "x") { component = v2.x; return true; }
                if (key == "y") { component = v2.y; return true; }
                if (value is NeoVector3Value v3 && key == "z")
                {
                    component = v3.z;
                    return true;
                }
                return false;
            }
            if (value is IDictionary<string, object?> dict)
            {
                bool isVector2 = IsVector2Value(dict, requireIntegers: false);
                bool isVector3 = IsVector3Value(dict, requireIntegers: false);
                if (!isVector2 && !isVector3) return false;
                if (key == "z" && !isVector3) return false;
                if (key != "x" && key != "y" && key != "z") return false;
                if (!dict.TryGetValue(key, out var raw)) return false;
                if (!TryAsDouble(raw, out double numeric)) return false;
                component = (float)numeric;
                return true;
            }
            return false;
        }

        /// <summary>
        /// P42 §3 / §1.1. Reads one colour channel — <c>r</c>, <c>g</c>,
        /// <c>b</c>, or <c>a</c> — off whatever CLR shape a Color receiver
        /// arrived as. The sibling of <see cref="TryReadVectorComponent"/>,
        /// and deliberately separate from it: a colour is four channels in
        /// <c>[0, 1]</c>, not a four-component vector, and merging the two
        /// would make <c>Position.a</c> and <c>Tint.z</c> silently readable.
        /// </summary>
        private static bool TryReadColorComponent(
            object? value,
            string key,
            out float component)
        {
            component = 0;
            if (key != "r" && key != "g" && key != "b" && key != "a")
            {
                return false;
            }
            if (value is Color color)
            {
                component = ReadColorChannel(color, key);
                return true;
            }
            if (value is NeoReadOnlyColor wrapper)
            {
                component = ReadColorChannel(wrapper.Value, key);
                return true;
            }
            if (value is NeoColorValue colorValue)
            {
                component = key switch
                {
                    "r" => colorValue.r,
                    "g" => colorValue.g,
                    "b" => colorValue.b,
                    _ => colorValue.a,
                };
                return true;
            }
            if (value is IDictionary<string, object?> dict && IsColorValue(dict))
            {
                if (!dict.TryGetValue(key, out var raw)) return false;
                if (!TryAsDouble(raw, out double numeric)) return false;
                component = (float)numeric;
                return true;
            }
            return false;
        }

        private static float ReadColorChannel(Color color, string key)
        {
            return key switch
            {
                "r" => color.r,
                "g" => color.g,
                "b" => color.b,
                _ => color.a,
            };
        }

        private static bool IsVector2Value(object? value, bool requireIntegers)
        {
            if (value is Vector2 vector2 && value is not Vector3)
            {
                return !requireIntegers || (IsInteger(vector2.x) && IsInteger(vector2.y));
            }
            if (value is Vector2Int && value is not Vector3Int) return true;
            if (value is NeoReadOnlyVector2 wrapper)
            {
                Vector2 vector = wrapper.Value;
                return !requireIntegers || (IsInteger(vector.x) && IsInteger(vector.y));
            }
            if (value is NeoReadOnlyVector2Int) return true;
            if (value is NeoVector2Value v2 && value is not NeoVector3Value)
            {
                return !requireIntegers || (IsInteger(v2.x) && IsInteger(v2.y));
            }
            if (value is IDictionary<string, object?> dict && dict.Count == 2)
            {
                return TryAsDouble(dict.TryGetValue("x", out var x) ? x : null, out double xv)
                    && TryAsDouble(dict.TryGetValue("y", out var y) ? y : null, out double yv)
                    && (!requireIntegers || (IsInteger(xv) && IsInteger(yv)));
            }
            return false;
        }

        private static bool IsVector3Value(object? value, bool requireIntegers)
        {
            if (value is Vector3 vector3)
            {
                return !requireIntegers ||
                    (IsInteger(vector3.x) && IsInteger(vector3.y) && IsInteger(vector3.z));
            }
            if (value is Vector3Int) return true;
            if (value is NeoReadOnlyVector3 wrapper)
            {
                Vector3 vector = wrapper.Value;
                return !requireIntegers ||
                    (IsInteger(vector.x) && IsInteger(vector.y) && IsInteger(vector.z));
            }
            if (value is NeoReadOnlyVector3Int) return true;
            if (value is NeoVector3Value v3)
            {
                return !requireIntegers ||
                    (IsInteger(v3.x) && IsInteger(v3.y) && IsInteger(v3.z));
            }
            if (value is IDictionary<string, object?> dict && dict.Count == 3)
            {
                return TryAsDouble(dict.TryGetValue("x", out var x) ? x : null, out double xv)
                    && TryAsDouble(dict.TryGetValue("y", out var y) ? y : null, out double yv)
                    && TryAsDouble(dict.TryGetValue("z", out var z) ? z : null, out double zv)
                    && (!requireIntegers || (IsInteger(xv) && IsInteger(yv) && IsInteger(zv)));
            }
            return false;
        }

        private static bool IsColorValue(object? value)
        {
            if (value is Color) return true;
            if (value is NeoReadOnlyColor) return true;
            if (value is NeoColorValue) return true;
            if (value is IDictionary<string, object?> dict && dict.Count == 4)
            {
                return TryAsDouble(dict.TryGetValue("r", out var r) ? r : null, out double rv)
                    && TryAsDouble(dict.TryGetValue("g", out var g) ? g : null, out double gv)
                    && TryAsDouble(dict.TryGetValue("b", out var b) ? b : null, out double bv)
                    && TryAsDouble(dict.TryGetValue("a", out var a) ? a : null, out double av)
                    && IsColorComponent(rv)
                    && IsColorComponent(gv)
                    && IsColorComponent(bv)
                    && IsColorComponent(av);
            }
            return false;
        }

        private static bool IsColorComponent(double value)
        {
            return value >= 0 && value <= 1;
        }

        private static bool IsInteger(double value)
        {
            return System.Math.Truncate(value) == value;
        }

        private static string ReceiverTypeName(object? receiver)
        {
            if (receiver is null) return "null";
            if (receiver is string) return "string";
            if (receiver is bool) return "boolean";
            if (TryAsDouble(receiver, out _)) return "number";
            if (receiver is object?[]) return "array";
            if (receiver is IDictionary<string, object?>) return "object";
            return receiver.GetType().Name;
        }

        // ---------------------------------------------------------------
        // Reference-equality lookups against the project's value rows.
        // Mirrors the TS-side reliance on `ctx.vm.values.find(r => r.value === value)`.
        // ---------------------------------------------------------------

        internal static string? FindRowClassIdByReference(object? value, Context ctx)
        {
            // Prefer the context's exact ownership-qualified reverse index.
            // The same stable id may legitimately exist in Session and Save
            // with different runtime classes; id-only lookup would select the
            // wrong overlay for an unwrapped NeoObjectRecord.
            if (TryFindRowReferenceByReference(value, ctx, out RowReference rowRef))
            {
                if (!ctx.client.TryGetValue(
                        rowRef.ownership,
                        rowRef.valueId,
                        out MemberValue? indexedRow))
                {
                    // Declaration-default rows are synthetic and
                    // intentionally do not live in the client's persisted
                    // value maps. Preserve the effective Class provenance
                    // captured while unwrapping so a readonly Class default
                    // typed as an abstract base can still dispatch through its
                    // concrete runtime override surface.
                    return rowRef.classId;
                }
                if (!string.IsNullOrEmpty(indexedRow.classId))
                {
                    return indexedRow.classId;
                }
                return ctx.client.TryInferMemberForValueId(
                        rowRef.valueId,
                        out JsonMember? indexedMember)
                    && indexedMember is ClassMember indexedClassMember
                        ? indexedClassMember.classId
                        : null;
            }
            if (value is INeoValueReference valueReference
                && !string.IsNullOrEmpty(valueReference.valueId))
            {
                NeoValueOwnership ownership = value is NeoGeneratedClassValue generated
                    ? generated.ValueOwnership
                    : ResolveOwnershipForValueId(ctx, valueReference.valueId!);
                if (!ctx.client.TryGetValue(
                        ownership,
                        valueReference.valueId!,
                        out MemberValue? referencedRow))
                {
                    return null;
                }
                if (!string.IsNullOrEmpty(referencedRow.classId)) return referencedRow.classId;
                if (ctx.client.TryInferMemberForValueId(
                        valueReference.valueId!, out JsonMember? referencedMember)
                    && referencedMember is ClassMember referencedClassMember)
                {
                    return referencedClassMember.classId;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------
        // Project enumeration helpers — wrap the NeoClient's keyed-by-id
        // dicts behind enumerable accessors so the evaluator (and helpers
        // like FindSchemaPlacement) can iterate.
        // ---------------------------------------------------------------

        private static IEnumerable<NeoSchemaClass> EnumerateClasses(NeoClient client)
        {
            foreach (NeoSchemaClass schemaClass in client.classes.Values) yield return schemaClass;
        }

        // Both EnumerateAllMembers and EnumerateAllValues need access
        // to the client's underlying maps. NeoClient currently doesn't
        // expose them as IEnumerable, so we'd need a small accessor
        // there. For the first cut, route through the public
        // ProjectData / ProjectSaveData since the evaluator itself
        // doesn't need the full set most of the time — just the
        // FindSchemaPlacement and FindRowClassIdByReference paths.
        //
        // Simplest fix: expose IReadOnlyDictionary-typed views on
        // NeoClient. Done in NeoClient updates below — see
        // `NeoClient.members` / `NeoClient.values` / `NeoClient.classes` /
        // `NeoClient.enums`.

        private static IEnumerable<KeyValuePair<string, JsonMember>> EnumerateAllMembers(NeoClient client)
        {
            foreach (var kvp in client.members) yield return kvp;
        }

        private static IEnumerable<KeyValuePair<string, MemberValue>> EnumerateAllValues(NeoClient client)
        {
            // Save-side wins by id (matches NeoClient.TryGetValue).
            var seen = new HashSet<string>();
            foreach (var kvp in client.sessionValues)
            {
                seen.Add(kvp.Key);
                yield return kvp;
            }
            foreach (var kvp in client.saveValues)
            {
                seen.Add(kvp.Key);
                yield return kvp;
            }
            foreach (var kvp in client.values)
            {
                if (seen.Contains(kvp.Key)) continue;
                yield return kvp;
            }
        }

        // ---------------------------------------------------------------
        // String formatting for `$"..."` interpolation
        // ---------------------------------------------------------------

        private static string FormatForInterp(object? value, TypeInfo sourceType, Context ctx)
        {
            if (value is null) return "";
            switch (sourceType.type)
            {
                case MemberKind.Enum:
                {
                    string enumId = (sourceType as EnumTypeInfo)?.enumId ?? "";
                    var ids = new List<string>();
                    if (value is object?[] arr)
                    {
                        foreach (var e in arr) if (e is string s) ids.Add(s);
                    }
                    if (!ctx.client.TryGetEnum(enumId, out JsonEnum? jsonEnum))
                    {
                        return string.Join(", ", ids);
                    }
                    var labels = new List<string>(ids.Count);
                    foreach (var id in ids)
                    {
                        if (!jsonEnum.options.TryGetValue(id, out EnumOption opt))
                        {
                            labels.Add(id);
                        }
                        else if (ctx.client.Localization.TryResolveText(opt.text, out var localized))
                        {
                            labels.Add(localized);
                        }
                        else
                        {
                            labels.Add(opt.text);
                        }
                    }
                    return string.Join(", ", labels);
                }
                case MemberKind.Class:
                {
                    string classId = (sourceType as ClassTypeInfo)?.classId ?? "";
                    string typeName = ctx.client.TryGetClass(classId, out NeoSchemaClass? ct)
                        ? ct.name
                        : classId;
                    string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                    return $"(Class<{typeName}>, Value<{rowId}>)";
                }
                case MemberKind.List:
                {
                    var entryType = (sourceType as CollectionTypeInfo)?.entryTypeInfo;
                    string entryName = entryType is null ? "unknown" : DescribeRuntimeType(entryType, ctx);
                    string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                    return $"(List<{entryName}>, Value<{rowId}>)";
                }
                case MemberKind.Dictionary:
                {
                    var entryType = (sourceType as CollectionTypeInfo)?.entryTypeInfo;
                    string entryName = entryType is null ? "unknown" : DescribeRuntimeType(entryType, ctx);
                    string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                    return $"(Dictionary<{entryName}>, Value<{rowId}>)";
                }
                default:
                    return value.ToString() ?? "";
            }
        }

        private static string DescribeRuntimeType(TypeInfo t, Context ctx)
        {
            switch (t.type)
            {
                case MemberKind.Null: return "null";
                case MemberKind.Bool: return "bool";
                case MemberKind.Int: return "int";
                case MemberKind.Float: return "float";
                case MemberKind.String: return "string";
                case MemberKind.Sprite: return "SpriteInfo";
                case MemberKind.Audio: return "AudioClipInfo";
                case MemberKind.Vector2: return "Vector2";
                case MemberKind.Vector2Int: return "Vector2Int";
                case MemberKind.Vector3: return "Vector3";
                case MemberKind.Vector3Int: return "Vector3Int";
                case MemberKind.Color: return "Color";
                case MemberKind.Class:
                {
                    string classId = (t as ClassTypeInfo)?.classId ?? "";
                    return ctx.client.TryGetClass(classId, out NeoSchemaClass? ct) ? ct.name : classId;
                }
                case MemberKind.Enum:
                {
                    string enumId = (t as EnumTypeInfo)?.enumId ?? "";
                    return ctx.client.TryGetEnum(enumId, out JsonEnum? je) ? je.name : enumId;
                }
                case MemberKind.List:
                {
                    var inner = (t as CollectionTypeInfo)?.entryTypeInfo;
                    return inner is null ? "List<unknown>" : $"List<{DescribeRuntimeType(inner, ctx)}>";
                }
                case MemberKind.Dictionary:
                {
                    var inner = (t as CollectionTypeInfo)?.entryTypeInfo;
                    return inner is null
                        ? "Dictionary<unknown>"
                        : $"Dictionary<{DescribeRuntimeType(inner, ctx)}>";
                }
                default: return "unknown";
            }
        }

        internal static string? FindRowIdByReference(object? value, Context ctx)
        {
            return TryFindRowReferenceByReference(value, ctx, out RowReference rowRef)
                ? rowRef.valueId
                : null;
        }

        internal static NeoValueOwnership? FindRowOwnershipByReference(object? value, Context ctx)
        {
            return TryFindRowReferenceByReference(value, ctx, out RowReference rowRef)
                ? rowRef.ownership
                : null;
        }

        private static bool TryFindRowReferenceByReference(
            object? value,
            Context ctx,
            out RowReference rowRef)
        {
            if (value is not null && ctx.rowReverseIndex.TryGetValue(value, out rowRef))
            {
                return true;
            }
            rowRef = default;
            return false;
        }

        private static string StringifyForInterp(object? v)
        {
            if (v is null) return "";
            if (v is string s) return s;
            if (v is bool b) return b ? "true" : "false";
            if (TryAsDouble(v, out double d)) return d.ToString(CultureInfo.InvariantCulture);
            return Newtonsoft.Json.JsonConvert.SerializeObject(v);
        }
    }
}
