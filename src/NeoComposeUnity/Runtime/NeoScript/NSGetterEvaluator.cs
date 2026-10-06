// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// The bound-delegate targets currently executing, innermost last.
    /// </summary>
    internal sealed class NeoDelegateFrameStack
    {
        internal const int MaxDepth = 64;

        // Parallel arrays, not a list of tuples: building, adding and
        // clearing a tuple frame each stored both references, and every
        // reference store pays a write barrier. A pop leaves its slot
        // behind, so re-entering the same frame stores nothing; the ids it
        // keeps reachable are the schema's and rows' own strings.
        private string[] memberIds = new string[4];
        private string?[] valueIds = new string?[4];
        private int count;

        internal int Count => count;

        internal string MemberId(int index) => memberIds[index];

        internal string? ValueId(int index) => valueIds[index];

        internal bool Contains(string memberId, string? valueId)
        {
            for (int i = 0; i < count; i++)
            {
                if (NSGetterEvaluator.SameId(memberIds[i], memberId)
                    && NSGetterEvaluator.SameId(valueIds[i], valueId))
                {
                    return true;
                }
            }
            return false;
        }

        internal void Push(string memberId, string? valueId)
        {
            if (count == memberIds.Length)
            {
                Array.Resize(ref memberIds, count * 2);
                Array.Resize(ref valueIds, count * 2);
            }
            if (!ReferenceEquals(memberIds[count], memberId))
                memberIds[count] = memberId;
            if (!ReferenceEquals(valueIds[count], valueId))
                valueIds[count] = valueId;
            count++;
        }

        internal void Pop() => count--;
    }

    /// <summary>
    /// Tracks Session-backed class values created by NeoScript constructor
    /// intrinsics for one logical invocation. Nested NSFunction/setter/getter
    /// executions share the tracker, so a temporary returned into its caller
    /// is not collected before the caller can attach or return it.
    /// </summary>
    internal sealed class NeoScriptAllocationTracker
    {
        // Most invocations only read existing rows; constructor bookkeeping is
        // needed only after an invocation actually creates a Session graph.
        private HashSet<string>? _allocatedRootIds;
        private HashSet<string> allocatedRootIds => _allocatedRootIds ??= new();
        private HashSet<string>? _escapedRootIds;
        private HashSet<string> escapedRootIds => _escapedRootIds ??= new();
        private HashSet<string>? _completedAllocationRootIds;
        private HashSet<string> completedAllocationRootIds => _completedAllocationRootIds ??= new();
        private Dictionary<string, string>? _constructedParentByChildId;
        private Dictionary<string, string> constructedParentByChildId => _constructedParentByChildId ??= new();
        private HashSet<string>? _parentlessAllocatedRootIds;
        private HashSet<string> parentlessAllocatedRootIds => _parentlessAllocatedRootIds ??= new();
        private int activeExecutions;
        private NeoTimestamp? constructionTimestamp;

        internal int ActiveExecutionCount => activeExecutions;
        /// <summary>
        /// Counts outermost executions, so a detached temporary created by an
        /// earlier one is never registered with a later one's cleanup.
        /// </summary>
        internal int Generation
        {
            get; private set;
        }
        // Conservative lifetime gate for direct synchronous frame reuse. Any
        // operation that can retain context state keeps its ordinary lifetime.
        internal bool ReusableContext = true;
        private NeoDelegateFrameStack? delegateFrames;
        internal NeoDelegateFrameStack DelegateFrames
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => delegateFrames ?? CreateDelegateFrames();
        }

        private NeoDelegateFrameStack CreateDelegateFrames() => delegateFrames = new();
        private List<NeoScriptObject>? listBuffered;

        internal void EnterExecution()
        {
            if (activeExecutions == 0)
            {
                Generation++;
                constructionTimestamp = null;
                _completedAllocationRootIds?.Clear();
                _constructedParentByChildId?.Clear();
                _parentlessAllocatedRootIds?.Clear();
            }
            activeExecutions++;
        }

        internal NeoTimestamp ConstructionTimestamp =>
            constructionTimestamp ??= NeoTimestamp.Now();

        internal void RegisterSessionRoot(string valueId)
        {
            ReusableContext = false;
            if (string.IsNullOrEmpty(valueId))
                return;
            allocatedRootIds.Add(valueId);
            if (!constructedParentByChildId.ContainsKey(valueId))
            {
                parentlessAllocatedRootIds.Add(valueId);
            }
        }

        internal bool IsAllocatedSessionRoot(string valueId) =>
            !string.IsNullOrEmpty(valueId)
            && (_allocatedRootIds?.Contains(valueId) == true
                || _completedAllocationRootIds?.Contains(valueId) == true);

        /// <summary>
        /// Records the owned edges a constructor graph already validated. The
        /// allocation tracker needs these only until the outermost NeoScript
        /// frame exits; retaining them lets escape/cleanup walk the just-built
        /// graph directly instead of rediscovering every parent by scanning the
        /// complete and continually growing Session store.
        /// </summary>
        internal void RegisterConstructedParents(
            Dictionary<string, string> parentByChildId)
        {
            foreach (var pair in parentByChildId)
            {
                RegisterConstructedParent(pair.Key, pair.Value);
            }
        }

        internal void RegisterConstructedParent(
            string childValueId,
            string parentValueId)
        {
            constructedParentByChildId[childValueId] = parentValueId;
            parentlessAllocatedRootIds.Remove(childValueId);
        }

        internal bool IsKnownParentlessAllocatedRoot(string valueId) =>
            _parentlessAllocatedRootIds?.Contains(valueId) == true;

        /// <summary>
        /// A fresh parentless constructor root was already validated and
        /// partition-stamped together with every owned descendant before it
        /// was published. Synchronous NeoScript mutation preserves those
        /// storage invariants, so attaching it without moving its root to a
        /// different partition/container can reuse that proof instead of
        /// walking the complete graph again.
        /// </summary>
        internal bool IsKnownNormalizedParentlessGraph(
            string valueId,
            MemberValue row,
            string? expectedMapKey,
            string? expectedContainerId) =>
            _parentlessAllocatedRootIds?.Contains(valueId) == true
            && row.mapKey == expectedMapKey
            && row.containerId == expectedContainerId;

        /// <summary>
        /// A value crossing a function-call boundary is no longer owned by
        /// the current NeoScript frame. Mark its complete constructed graph as
        /// escaped; a later parent assignment may still move/import it.
        /// </summary>
        internal void MarkEscaped(object? value, NSGetterEvaluator.Context ctx)
        {
            // Most getters return existing rows. With no temporary roots,
            // there is nothing to retain and no owned-parent graph to scan.
            if (_allocatedRootIds is null || _allocatedRootIds.Count == 0)
                return;
            MarkEscaped(value, ctx, new HashSet<object>(), new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal));
        }

        private void MarkEscaped(
            object? value,
            NSGetterEvaluator.Context ctx,
            HashSet<object> visited,
            Dictionary<string, HashSet<string>?> ownedChildren)
        {
            if (value is null || value is string || value.GetType().IsValueType)
            {
                return;
            }
            if (!visited.Add(value))
                return;
            if (value is NeoScriptObject detached)
            {
                // A detached graph holds no rows yet; an attached one escapes
                // through its root like any constructed row.
                if (detached.attachedId is not null)
                    MarkAllocationGroupEscaped(detached.attachedId, ctx, ownedChildren);
                return;
            }

            string? valueId = NSGetterEvaluator.FindRowIdByReference(value, ctx);
            if (valueId is not null)
            {
                MarkAllocationGroupEscaped(valueId, ctx, ownedChildren);
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    MarkEscaped(entry.Key, ctx, visited, ownedChildren);
                    MarkEscaped(entry.Value, ctx, visited, ownedChildren);
                }
                return;
            }
            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (object? entry in enumerable)
                {
                    MarkEscaped(entry, ctx, visited, ownedChildren);
                }
            }
        }

        private void MarkAllocationGroupEscaped(
            string valueId,
            NSGetterEvaluator.Context ctx,
            Dictionary<string, HashSet<string>?> ownedChildren)
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
                if (!TryFindConstructedOrStoredParent(
                        ctx.client,
                        cursor,
                        ownedChildren,
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

        /// <param name="terminalResult">The body's result; <c>default</c>, a fallthrough, when it has none.</param>
        internal void ExitExecution(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            in NeoScriptExecutionResult terminalResult)
        {
            if (activeExecutions <= 0)
            {
                throw new InvalidOperationException(
                    "NeoScript allocation tracker execution depth underflow.");
            }
            activeExecutions--;
            if (activeExecutions != 0)
                return;
            SealListBuffers();
            if (_allocatedRootIds is null)
                return;
            // Reclaiming decides from where the roots live now.
            client.CommitScriptWrites();

            if (terminalResult.Returned)
            {
                MarkEscaped(terminalResult.ReturnValue, ctx);
            }

            completedAllocationRootIds.UnionWith(allocatedRootIds);

            var ownedChildren = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
            foreach (string valueId in allocatedRootIds.ToArray())
            {
                // A root a write moved out of Session left nothing to reclaim,
                // and proving it parentless would search its new parent.
                if (!client.HoldsSessionRoot(valueId))
                    continue;
                // Strict construction ownership is a tree. A constructor root
                // with any live owned parent is therefore reclaimed or kept as
                // part of that parent's top-level graph; evaluating its whole
                // ancestor chain again would duplicate the same decision for
                // every nested constructor in an eager initializer.
                bool hasParent = TryFindConstructedOrStoredParent(
                        client,
                        valueId,
                        ownedChildren,
                        out string? _);
                if (hasParent)
                {
                    continue;
                }
                if (escapedRootIds.Contains(valueId))
                    continue;
                // External/global owners were ruled out above. Force-reclaim
                // the invocation-minted owned graph instead of ordinary
                // reachability GC: storage-key rows in unloaded partitions
                // are conservatively protected by normal GC, but these fresh
                // rows cannot belong to an unloaded authored graph.
                IReadOnlyCollection<string> removed =
                    client.RemoveTemporaryWritableValueGraph(
                        NeoValueOwnership.Session,
                        valueId);
                if (removed.Count == 0)
                    continue;
                client.DisposeWrappersTouchingRows(removed);
                NSGetterEvaluator.EvictCachedRows(
                    ctx,
                    NeoValueOwnership.Session,
                    removed);
            }
            _allocatedRootIds?.Clear();
            _escapedRootIds?.Clear();
            _constructedParentByChildId?.Clear();
            _parentlessAllocatedRootIds?.Clear();
        }

        /// <summary>
        /// Remembers a detached object that took an append buffer, so the
        /// outermost exit seals it. Outside any execution nothing exits; the
        /// object's next read seals it instead.
        /// </summary>
        internal void NoteListBuffer(NeoScriptObject value)
        {
            if (activeExecutions == 0 || value.listBuffersNoted)
                return;
            value.listBuffersNoted = true;
            (listBuffered ??= new()).Add(value);
        }

        private void SealListBuffers()
        {
            if (listBuffered is null)
                return;
            for (int index = 0; index < listBuffered.Count; index++)
            {
                NeoScriptObject value = listBuffered[index];
                value.listBuffersNoted = false;
                NeoGeneratedTypesSupport.SealDetachedLists(value);
            }
            listBuffered.Clear();
        }

        private bool TryFindConstructedOrStoredParent(
            NeoClient client,
            string childValueId,
            Dictionary<string, HashSet<string>?> ownedChildren,
            out string? parentValueId)
        {
            if (constructedParentByChildId.TryGetValue(
                    childValueId,
                    out string constructedParent)
                && client.StillHasOwnedChildReference(
                    NeoValueOwnership.Session,
                    constructedParent,
                    childValueId,
                    ownedChildren))
            {
                parentValueId = constructedParent;
                return true;
            }
            constructedParentByChildId.Remove(childValueId);
            return client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                childValueId,
                out parentValueId);
        }
    }

    /// <summary>
    /// Optional P58 proof counters. Production contexts leave this unset;
    /// performance fixtures attach one to verify operator-local preparation.
    /// </summary>
    internal sealed class CollectionCallbackPreparationMetrics
    {
        internal int BodyValidations
        {
            get; set;
        }
        internal int BindingPlanCreations
        {
            get; set;
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
    public static partial class NSGetterEvaluator
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            object, Dictionary<string, int>> ListIdentityIndexes = new();
        // A Where result re-emits its source's value ids, and a mutated local
        // copy keeps them, so their entries keep the source's entry member.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            object, JsonMember> DerivedEntryMembers = new();

        /// <summary>
        /// Per-evaluation context: the project, the bound
        /// <c>__this__</c> / <c>__root__</c> values, and a cycle-detection
        /// stack of NSProperty member ids currently being evaluated.
        /// </summary>
        public class Context
        {
            internal sealed class CallFrameStack : IReadOnlyList<string>
            {
                private readonly IReadOnlyList<string> parent;
                private readonly string value;
                // Frames never change and call paths repeat, so a frame keeps
                // the frames pushed from it: one per distinct callee. Callees
                // are schema ids and names, so the set is bounded; a frame
                // with more than a few dozen, like the root every class
                // constructs from, keeps the rest by name. A reference scan
                // of those few dozen is cheaper than hashing an id.
                private CallFrameStack?[]? children;
                private Dictionary<string, CallFrameStack>? wideChildren;
                private const int ScannedChildren = 32;

                private CallFrameStack(
                    IReadOnlyList<string> parent,
                    string value)
                {
                    this.parent = parent;
                    this.value = value;
                    Count = parent.Count + 1;
                }

                private CallFrameStack()
                {
                    parent = Array.Empty<string>();
                    value = null!;
                }

                /// <summary>
                /// An empty stack. A context starting from a client's root
                /// shares its first frames too, instead of allocating them.
                /// </summary>
                internal static CallFrameStack CreateRoot() => new();

                /// <summary>Walks the frames once; indexing each would walk its parents again.</summary>
                internal bool Contains(string item)
                {
                    IReadOnlyList<string> frames = this;
                    while (frames is CallFrameStack frame)
                    {
                        // A root holds no frames, and nothing under it does.
                        if (frame.Count == 0)
                            return false;
                        if (frame.value == item)
                            return true;
                        frames = frame.parent;
                    }
                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (frames[i] == item)
                            return true;
                    }
                    return false;
                }

                private const int SiteFrames = 8;

                /// <summary>
                /// <see cref="Push(IReadOnlyList{string}, string)"/> for one
                /// site, which keeps the frames it pushed, one per parent: a
                /// parent too wide to scan, like the root every class
                /// constructs from, would otherwise hash the value.
                /// </summary>
                internal static IReadOnlyList<string> Push(
                    IReadOnlyList<string> parent,
                    string value,
                    ref CallFrameStack?[]? siteFrames)
                {
                    CallFrameStack?[] frames = siteFrames ??= new CallFrameStack?[SiteFrames];
                    int used = 0;
                    for (; used < frames.Length && frames[used] is { } frame; used++)
                    {
                        if (ReferenceEquals(frame.parent, parent) && ReferenceEquals(frame.value, value))
                            return frame;
                    }
                    IReadOnlyList<string> pushed = Push(parent, value);
                    if (used < frames.Length && pushed is CallFrameStack stack)
                        frames[used] = stack;
                    return pushed;
                }

                internal static IReadOnlyList<string> Push(IReadOnlyList<string> parent, string value)
                {
                    if (parent is not CallFrameStack frame)
                        return new CallFrameStack(parent, value);
                    CallFrameStack?[] children = frame.children ??= new CallFrameStack?[4];
                    // A call site pushes the same id instance every time, so
                    // a reference scan finds it without comparing contents.
                    int used = 0;
                    for (; used < children.Length && children[used] is { } child; used++)
                    {
                        if (ReferenceEquals(child.value, value))
                            return child;
                    }
                    if (frame.wideChildren is not null && frame.wideChildren.TryGetValue(value, out CallFrameStack? wide))
                        return wide;
                    for (int i = 0; i < used; i++)
                    {
                        if (children[i]!.value == value)
                            return children[i]!;
                    }
                    var pushed = new CallFrameStack(frame, value);
                    if (used == children.Length && used < ScannedChildren)
                    {
                        Array.Resize(ref frame.children, used * 2);
                        children = frame.children;
                    }
                    if (used < children.Length)
                        children[used] = pushed;
                    else
                        (frame.wideChildren ??= new Dictionary<string, CallFrameStack>()).Add(value, pushed);
                    return pushed;
                }

                public int Count
                {
                    get;
                }

                public string this[int index] => index == Count - 1
                    ? value
                    : parent[index];

                public IEnumerator<string> GetEnumerator()
                {
                    for (int index = 0; index < Count; index++)
                    {
                        yield return this[index];
                    }
                }

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            /// <summary>
            /// The call and object-initializer handlers an expression context
            /// runs with. One reference, so a frame installs and restores both
            /// with one store, and one object rather than a delegate per handler.
            /// </summary>
            internal abstract class ExpressionHandlers
            {
                internal abstract object? Call(CallFunctionPointer pointer, NeoScriptScope scope, Context ctx);

                internal abstract object? Initialize(ObjectInitializerPointer pointer, NeoScriptScope scope, Context ctx);
            }

            public NeoClient client
            {
                get;
            }
            public object? thisValue
            {
                get => thisValueField;
                private set => thisValueField = value;
            }
            // A plain field so ClearDirectInvocation's null store pays no
            // write barrier; one through the setter does.
            private object? thisValueField;
            public object? rootValue
            {
                get; private set;
            }
            public object? contextValue
            {
                get; private set;
            }
            public INeoDialogueMemoryStore? memoryStore
            {
                get; private set;
            }
            /// <summary>
            /// Stack of NSProperty member ids currently in-flight. Threaded
            /// through callGetter recursion via fresh-copy children so a
            /// cycle (`A.x` calls `B.y` calls `A.x` on a different receiver)
            /// trips before the runtime stack overflows.
            /// </summary>
            public IReadOnlyCollection<string> getterCallStack
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => getterCallStackFrame < 0
                    ? getterCallStackField
                    : frameStack!.frames[getterCallStackFrame].getterCallStack!;
            }
            private IReadOnlyCollection<string> getterCallStackField;
            // The frame holding the running getter's stack, or -1 for the
            // field, for the same reason as functionCallStackFrame.
            private int getterCallStackFrame = -1;
            /// <summary>
            /// Stack of NSProperty member ids whose setters are currently
            /// executing. Kept separate from <see cref="getterCallStack"/>,
            /// but preserved by every child context so setter→getter→setter
            /// recursion is detected by the shared NeoScript executor.
            /// </summary>
            public IReadOnlyCollection<string> setterCallStack
            {
                get; private set;
            }
            /// <summary>
            /// Ordered stack of NSFunction member ids currently executing.
            /// Unlike getter/setter cycle sets, recursion is valid and is only
            /// rejected once the runtime depth cap is reached.
            /// </summary>
            public IReadOnlyList<string> functionCallStack => CurrentFunctionCallStack ?? NoFunctionCalls;
            // Null while no function runs, so ClearDirectInvocation's constant
            // null store pays no write barrier; storing the empty stack does.
            private IReadOnlyList<string>? functionCallStackField;
            // The frame holding the running function's stack, or -1 for
            // functionCallStackField. A frame at one depth usually enters the
            // same stack again, so keeping it there skips the write barriers
            // that storing it here, and restoring the caller's, pay.
            private int functionCallStackFrame = -1;
            private IReadOnlyList<string>? CurrentFunctionCallStack
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => functionCallStackFrame < 0
                    ? functionCallStackField
                    : frameStack!.frames[functionCallStackFrame].functionCallStack;
            }
            // Skips the interface call Count goes through for the usual
            // frame stack and for an empty one.
            internal int functionDepth
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    IReadOnlyList<string>? stack = CurrentFunctionCallStack;
                    return stack is CallFrameStack frames
                        ? frames.Count
                        : stack?.Count ?? 0;
                }
            }
            // A field load, where each Array.Empty call goes through a stub.
            private static readonly string[] NoFunctionCalls = System.Array.Empty<string>();
            /// <summary>
            /// Ordered bound-delegate targets currently executing. The
            /// allocation tracker shares it, so nested evaluator contexts
            /// retain cycle detection across closure and member-target
            /// boundaries.
            /// </summary>
            internal NeoDelegateFrameStack delegateCallStack
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => allocationTracker.DelegateFrames;
            }
            /// <summary>
            /// P43 §7.2.3 — ordered names of the classes currently under
            /// construction. Deliberately separate from
            /// <see cref="functionCallStack"/>: a constructor chain recurses
            /// through member initializers and nested <c>new</c> expressions
            /// rather than through NSFunction calls, so the NSFunction cap
            /// never sees it. Bounded by
            /// <see cref="NeoGeneratedTypesSupport.MaxConstructionDepth"/>.
            /// </summary>
            public IReadOnlyList<string> constructionStack
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => constructionStackFrame < 0
                    ? constructionStackField
                    : frameStack!.frames[constructionStackFrame].constructionStack!;
            }
            private IReadOnlyList<string> constructionStackField;
            // The frame holding the running construction's stack, or -1 for
            // the field, for the same reason as functionCallStackFrame.
            private int constructionStackFrame = -1;
            internal NeoValueOwnership valueOwnership
            {
                get;
            }

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
            /// via <see cref="WithThis"/> and the frames run on it
            /// so a callGetter's inner evaluation sees the same row
            /// identities the outer evaluation built up.
            /// </summary>
            internal Dictionary<RowCacheKey, object?> rowUnwrapCache
            {
                get;
            }

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
            internal ConditionalWeakTable<object, RowReference> rowReverseIndex
            {
                get;
            }
            internal Dictionary<RowKey, HashSet<RowCacheKey>> rowCacheKeysByRow
            {
                get;
            }

            // Shared with the context's family, like its frames.
            private CollectionRowCache? collectionRows;

            /// <summary>The row a list or dictionary aliases, or null.</summary>
            internal RowReference? CollectionRowReference(object collection) =>
                (collectionRows ??= new CollectionRowCache()).Find(collection, rowReverseIndex);

            /// <summary>The detached slot a list aliases, or null.</summary>
            internal NeoGeneratedTypesSupport.DetachedArrayOrigin? ArrayDetachedOrigin(object?[] array) =>
                (collectionRows ??= new CollectionRowCache()).Detached(array, rowReverseIndex);

            /// <summary>
            /// Records a list the evaluator just allocated, so its reads skip
            /// both alias lookups. Only a list no unwrap or slot has seen
            /// qualifies: those register an alias without moving the epoch.
            /// </summary>
            internal void NoteFreshList(object?[] list)
            {
                if (list.Length > 0)
                    (collectionRows ??= new CollectionRowCache()).NoteFresh(list, rowReverseIndex);
            }
            // The shared table's entry for rowReverseIndex, which never
            // changes and is never removed, so the context keeps it rather
            // than taking the table's lock on every write.
            private RowAliasIndex? rowAliasIndex;
            internal RowAliasIndex rowAliases =>
                rowAliasIndex ??= RowAliasIndexes.GetValue(rowReverseIndex, CreateRowAliasIndexCallback);

            /// <summary>The alias index, if a context sharing this reverse index built it.</summary>
            internal RowAliasIndex? BuiltRowAliases =>
                rowAliasIndex ?? (RowAliasIndexes.TryGetValue(rowReverseIndex, out RowAliasIndex built)
                    ? rowAliasIndex = built
                    : null);
            internal ExpressionHandlers? expressionHandlers
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => expressionHandlersFrame < 0
                    ? expressionHandlersField
                    : frameStack!.frames[expressionHandlersFrame].expressionHandlers;
                private set => expressionHandlersField = value;
            }
            // A plain field for the same reason as thisValueField.
            private ExpressionHandlers? expressionHandlersField;
            // The frame holding the bound handlers, or -1 for the field, for
            // the same reason as functionCallStackFrame.
            private int expressionHandlersFrame = -1;
            /// <summary>
            /// True while a constructor body's own statements run. The body
            /// runs immediate, but a property it assigns gets a setter frame
            /// that may call a deferred Function. Frames, forks and collection
            /// callbacks clear it.
            /// </summary>
            internal bool constructorBody;
            internal Dictionary<string, SchemaPlacement?> schemaPlacementCache
            {
                get;
            }
            internal Dictionary<(string classId, string schemaKey), string?> callableDispatchCache
            {
                get;
            }
            private Dictionary<string, IReadOnlyDictionary<string, NeoGenericEnvEntry>>? genericEnvironmentCacheStore;
            // Most invocations never resolve a receiver-bound generic
            // signature, so the cache is created on first use. Frames forked
            // before that keep their own; the resolution is a pure function
            // of the receiver, so a miss only repeats work.
            internal Dictionary<string, IReadOnlyDictionary<string, NeoGenericEnvEntry>>
                genericEnvironmentCache => genericEnvironmentCacheStore ??= new(System.StringComparer.Ordinal);
            internal NeoScriptAllocationTracker allocationTracker
            {
                get; private set;
            }
            internal ClassMember? initializerPlacement
            {
                get; set;
            }
            internal bool TryGetConstructionClassContext(
                string classId,
                out IReadOnlyDictionary<string, GenericBinding>? classArguments,
                out IReadOnlyDictionary<string, string>? storedGenericBindings)
            {
                if (initializerPlacement?.classId == classId
                    && initializerPlacement.classArguments is not null)
                {
                    classArguments = initializerPlacement.classArguments;
                    storedGenericBindings = null;
                    return true;
                }
                return client.TryGetReplayingVirtualInstanceClassContext(
                    classId, out classArguments, out storedGenericBindings);
            }
            internal CollectionCallbackPreparationMetrics?
                collectionCallbackPreparationMetrics
            {
                get; set;
            }

            public Context(
                NeoClient client,
                object? thisValue,
                object? rootValue,
                object? contextValue = null,
                INeoDialogueMemoryStore? memoryStore = null,
                IReadOnlyCollection<string>? getterCallStack = null,
                Dictionary<RowCacheKey, object?>? rowUnwrapCache = null,
                ConditionalWeakTable<object, RowReference>? rowReverseIndex = null,
                NeoValueOwnership valueOwnership = NeoValueOwnership.Save,
                IReadOnlyCollection<string>? setterCallStack = null,
                IReadOnlyList<string>? functionCallStack = null,
                Dictionary<string, SchemaPlacement?>? schemaPlacementCache = null,
                Dictionary<(string classId, string schemaKey), string?>? callableDispatchCache = null,
                Dictionary<RowKey, HashSet<RowCacheKey>>? rowCacheKeysByRow = null,
                Dictionary<
                    string,
                    IReadOnlyDictionary<string, NeoGenericEnvEntry>>?
                    genericEnvironmentCache = null,
                IReadOnlyList<string>? constructionStack = null)
            {
                this.client = client;
                this.thisValue = thisValue;
                this.rootValue = rootValue;
                this.contextValue = contextValue;
                this.memoryStore = memoryStore;
                getterCallStackField = getterCallStack ?? client.EmptyCallFrames;
                this.setterCallStack = setterCallStack ?? client.EmptyCallFrames;
                this.rowUnwrapCache = rowUnwrapCache ?? new Dictionary<RowCacheKey, object?>();
                this.rowReverseIndex = rowReverseIndex
                    ?? new ConditionalWeakTable<object, RowReference>();
                this.rowCacheKeysByRow = rowCacheKeysByRow
                    ?? new Dictionary<RowKey, HashSet<RowCacheKey>>();
                this.valueOwnership = valueOwnership;
                functionCallStackField = functionCallStack;
                // Placements depend on the exported schema, not the receiver or
                // invocation. Share them across getters and clear with schema caches.
                this.schemaPlacementCache = schemaPlacementCache
                    ?? client.ScriptSchemaPlacements;
                this.callableDispatchCache = callableDispatchCache
                    ?? client.ScriptCallableDispatch;
                genericEnvironmentCacheStore = genericEnvironmentCache;
                constructionStackField = constructionStack
                    ?? client.EmptyCallFrames;
                allocationTracker = new NeoScriptAllocationTracker();
            }

            // Invocation-local caches, allocations, and handlers intentionally stay shared.
            // Only receiver bindings and immutable call stacks differ between frames.
            private Context Fork()
            {
                allocationTracker.ReusableContext = false;
                // Shared, like ShareFrames' family: a fork of its own would
                // build a fresh cache on its first collection read.
                collectionRows ??= new CollectionRowCache();
                var fork = (Context)MemberwiseClone();
                fork.constructorBody = false;
                // A fork can outlive the frames it was made in.
                if (functionCallStackFrame >= 0)
                {
                    fork.functionCallStackField = CurrentFunctionCallStack;
                    fork.functionCallStackFrame = -1;
                }
                if (expressionHandlersFrame >= 0)
                {
                    fork.expressionHandlersField = expressionHandlers;
                    fork.expressionHandlersFrame = -1;
                }
                if (getterCallStackFrame >= 0)
                {
                    fork.getterCallStackField = getterCallStack;
                    fork.getterCallStackFrame = -1;
                }
                if (constructionStackFrame >= 0)
                {
                    fork.constructionStackField = constructionStack;
                    fork.constructionStackFrame = -1;
                }
                return fork;
            }

            internal void ClearDirectInvocation()
            {
                thisValueField = null;
                // These are properties: a null store through an inlined
                // setter still pays a write barrier, so skip clear ones.
                if (contextValue is not null)
                    contextValue = null;
                if (initializerPlacement is not null)
                    initializerPlacement = null;
                // A C# setter entered its setter here.
                if (!ReferenceEquals(setterCallStack, client.EmptyCallFrames))
                    setterCallStack = client.EmptyCallFrames;
                genericEnvironmentCacheStore?.Clear();
                immediateExpressionContext = null;
                immediateExpressionSource = null;
                immediateExpressionState = null;
                immediateExpressionOptions = null;
                // The function binding stays: the next direct function call
                // usually binds the same call stack and handlers, and
                // skipping those stores skips their write barriers. Any
                // other renter clears it first.
            }

            /// <summary>Drops the call stack and handlers a direct function call left bound.</summary>
            internal void ClearFunctionBinding()
            {
                functionCallStackField = null;
                expressionHandlersField = null;
            }

            /// <summary>
            /// Binds the root or receiver on a context no frame has seen yet.
            /// The fork-per-binding helpers below exist for contexts already in
            /// use; a freshly created one is bound in place.
            /// </summary>
            internal void BindRoot(object? value) => rootValue = value;
            internal void BindThis(object? value) => thisValue = value;

            // Only for a newly created direct-call context that no frame has seen.
            internal void BindFunction(IReadOnlyList<string> directCallStack, object? receiver)
            {
                if (!ReferenceEquals(functionCallStackField, directCallStack))
                    functionCallStackField = directCallStack;
                // A pooled context comes back with a null receiver, which a
                // static call keeps: skip that store's write barrier.
                if (!ReferenceEquals(thisValueField, receiver))
                    thisValueField = receiver;
            }


            /// <summary>
            /// The immediate-mode expression context built from THIS frame, so
            /// nested statement blocks (if/else branches, loop bodies) reuse it
            /// instead of forking a context and two handler closures per block.
            /// <see cref="immediateExpressionSource"/> pins the owner: a fork
            /// copies these fields but fails the identity check and rebuilds.
            /// </summary>
            internal Context? immediateExpressionContext;
            internal Context? immediateExpressionSource;
            internal object? immediateExpressionState;
            internal object? immediateExpressionOptions;

            internal Context WithThis(object? value)
            {
                Context child = Fork();
                child.thisValue = value;
                return child;
            }

            internal Context WithRoot(object? value)
            {
                Context child = Fork();
                child.rootValue = value;
                return child;
            }

            internal Context WithContext(object? value)
            {
                Context child = Fork();
                child.contextValue = value;
                return child;
            }

            internal Context WithMemoryStore(INeoDialogueMemoryStore? value)
            {
                Context child = Fork();
                child.memoryStore = value;
                return child;
            }

            internal Context WithExpressionHandlers(ExpressionHandlers handlers)
            {
                Context child = Fork();
                child.expressionHandlers = handlers;
                return child;
            }

            internal Context WithSetterPushed(string memberId, object? receiver)
            {
                Context child = Fork();
                child.PushSetter(memberId, receiver);
                return child;
            }

            /// <summary>Enters a setter on a context its caller owns outright, without a fork.</summary>
            internal void PushSetter(string memberId, object? receiver)
            {
                setterCallStack = CallFrameStack.Push(
                    setterCallStack as IReadOnlyList<string> ?? setterCallStack.ToArray(), memberId);
                thisValue = receiver;
            }

            internal Context WithFunctionPushed(string memberId, IReadOnlyList<string> directCallStack, object? receiver)
            {
                Context child = Fork();
                child.functionCallStackField = PushFunction(child.functionCallStackField, memberId, directCallStack);
                child.thisValue = receiver;
                return child;
            }

            /// <summary>
            /// The state a synchronous frame changes on the context it runs in,
            /// restored by <see cref="ExitFunction"/>. Saved frames live on a
            /// <see cref="FrameStack"/> rather than in each caller's locals,
            /// which Mono zeroes on every call.
            /// </summary>
            internal struct FunctionFrame
            {
                internal object? thisValue;
                // The function stack and handlers the frame entered, and the
                // frames holding the caller's.
                internal IReadOnlyList<string>? functionCallStack;
                internal ExpressionHandlers? expressionHandlers;
                internal int callerFunctionCallStackFrame;
                internal int callerExpressionHandlersFrame;
                internal int callerGetterCallStackFrame;
                internal int callerConstructionStackFrame;
                // Whether the frame replaced this: most calls keep it, and
                // saving it anyway write-barriers.
                internal bool thisSaved;
                internal Context? immediateExpressionContext;
                internal Context? immediateExpressionSource;
                internal object? immediateExpressionState;
                internal object? immediateExpressionOptions;
                internal ClassMember? initializerPlacement;
                // Entered by getter and construction frames only.
                internal IReadOnlyCollection<string>? getterCallStack;
                internal IReadOnlyList<string>? constructionStack;
                internal bool constructorBody;
            }

            /// <summary>
            /// Saved frames, innermost last. Frames complete before their
            /// callers continue, so contexts sharing a row cache, which already
            /// run on one thread, share one stack. Exit clears a frame, so an
            /// entered one starts empty, except for its call stacks, handlers
            /// and saved <c>this</c>: those stay for the next frame at that
            /// depth, which usually stores the same ones and so skips their
            /// write barriers.
            /// </summary>
            internal sealed class FrameStack
            {
                internal FunctionFrame[] frames = new FunctionFrame[8];
                internal int depth;
            }

            private FrameStack? frameStack;

            private FrameStack Frames => frameStack ??= new FrameStack();

            /// <summary>Runs this new context's frames on <paramref name="family"/>'s stack.</summary>
            internal void ShareFrames(Context family)
            {
                frameStack = family.Frames;
                collectionRows = family.collectionRows ??= new CollectionRowCache();
            }

            internal void ShareAllocations(Context family) => allocationTracker = family.allocationTracker;

            /// <summary>
            /// Runs a synchronous function frame on this context instead of a
            /// fork: nothing retains a frame that completes before its caller
            /// continues, so the caller's state only needs to come back.
            /// Returns the frame <see cref="ExitFunction"/> restores.
            /// </summary>
            internal int EnterFunction(string memberId, IReadOnlyList<string> directCallStack, object? receiver)
            {
                IReadOnlyList<string> stack = PushFunction(CurrentFunctionCallStack, memberId, directCallStack);
                int frame = EnterThis(receiver);
                ref FunctionFrame entered = ref frameStack!.frames[frame];
                if (!ReferenceEquals(entered.functionCallStack, stack))
                    entered.functionCallStack = stack;
                functionCallStackFrame = frame;
                return frame;
            }

            /// <summary>
            /// <see cref="EnterFunction"/> without a call-stack frame, for a
            /// synchronous body that runs against another <c>this</c>.
            /// </summary>
            internal int EnterThis(object? receiver)
            {
                FrameStack stack = Frames;
                int frame = stack.depth;
                if (frame == stack.frames.Length)
                    Array.Resize(ref stack.frames, frame * 2);
                stack.depth = frame + 1;
                // Mono write-barriers every reference stored here: the
                // usually-null fields keep the null exit left them.
                ref FunctionFrame saved = ref stack.frames[frame];
                saved.callerFunctionCallStackFrame = functionCallStackFrame;
                saved.callerExpressionHandlersFrame = expressionHandlersFrame;
                saved.callerGetterCallStackFrame = getterCallStackFrame;
                saved.callerConstructionStackFrame = constructionStackFrame;
                // The immediate expression fields are set and cleared together.
                if (immediateExpressionContext is not null)
                {
                    saved.immediateExpressionContext = immediateExpressionContext;
                    saved.immediateExpressionSource = immediateExpressionSource;
                    saved.immediateExpressionState = immediateExpressionState;
                    saved.immediateExpressionOptions = immediateExpressionOptions;
                    immediateExpressionContext = null;
                    immediateExpressionSource = null;
                    immediateExpressionState = null;
                    immediateExpressionOptions = null;
                }
                if (initializerPlacement is not null)
                    saved.initializerPlacement = initializerPlacement;
                saved.constructorBody = constructorBody;
                constructorBody = false;
                if (!ReferenceEquals(thisValue, receiver))
                {
                    if (!ReferenceEquals(saved.thisValue, thisValue))
                        saved.thisValue = thisValue;
                    saved.thisSaved = true;
                    thisValue = receiver;
                }
                return frame;
            }

            /// <summary>
            /// Installs a function body's expression handlers inside
            /// <paramref name="frame"/>, which restores the caller's on exit;
            /// -1 binds them on a context no frame restores.
            /// </summary>
            internal void BindFrameHandlers(int frame, ExpressionHandlers handlers)
            {
                // Every call rebinds the same prebuilt handlers; an unchanged
                // field skips its write barrier.
                if (ReferenceEquals(expressionHandlers, handlers))
                    return;
                if (frame >= 0)
                {
                    ref FunctionFrame entered = ref frameStack!.frames[frame];
                    if (!ReferenceEquals(entered.expressionHandlers, handlers))
                        entered.expressionHandlers = handlers;
                    expressionHandlersFrame = frame;
                }
                else
                {
                    expressionHandlersField = handlers;
                }
            }

            /// <summary>
            /// A call from an empty stack takes the function's prebuilt
            /// one-frame stack: only pushed frames retain their callees.
            /// </summary>
            private static IReadOnlyList<string> PushFunction(
                IReadOnlyList<string>? stack,
                string memberId,
                IReadOnlyList<string> directCallStack) =>
                stack is null || stack.Count == 0 ? directCallStack : CallFrameStack.Push(stack, memberId);

            /// <summary>Restores the state <paramref name="frame"/> saved.</summary>
            internal void ExitFunction(int frame)
            {
                // Every frame is exited in a finally, so frame is the top one.
                FrameStack stack = frameStack!;
                stack.depth = frame;
                ref FunctionFrame saved = ref stack.frames[frame];
                // Most fields come back unchanged. Skipping those writes skips
                // their GC write barriers, which cost more than the compare.
                // Clearing the frame stores constant nulls, which skip them,
                // and so does restoring a caller's null.
                if (saved.thisSaved)
                {
                    if (saved.thisValue is { } callerThis)
                        thisValueField = callerThis;
                    else
                        thisValueField = null;
                    saved.thisSaved = false;
                }
                functionCallStackFrame = saved.callerFunctionCallStackFrame;
                expressionHandlersFrame = saved.callerExpressionHandlersFrame;
                getterCallStackFrame = saved.callerGetterCallStackFrame;
                constructionStackFrame = saved.callerConstructionStackFrame;
                if (!ReferenceEquals(immediateExpressionContext, saved.immediateExpressionContext))
                    immediateExpressionContext = saved.immediateExpressionContext;
                if (!ReferenceEquals(immediateExpressionSource, saved.immediateExpressionSource))
                    immediateExpressionSource = saved.immediateExpressionSource;
                if (!ReferenceEquals(immediateExpressionState, saved.immediateExpressionState))
                    immediateExpressionState = saved.immediateExpressionState;
                if (!ReferenceEquals(immediateExpressionOptions, saved.immediateExpressionOptions))
                    immediateExpressionOptions = saved.immediateExpressionOptions;
                if (saved.immediateExpressionContext is not null)
                {
                    saved.immediateExpressionContext = null;
                    saved.immediateExpressionSource = null;
                    saved.immediateExpressionState = null;
                    saved.immediateExpressionOptions = null;
                }
                if (!ReferenceEquals(initializerPlacement, saved.initializerPlacement))
                    initializerPlacement = saved.initializerPlacement;
                saved.initializerPlacement = null;
                constructorBody = saved.constructorBody;
            }

            /// <summary>
            /// Opens a getter frame on this context instead of a fork: a
            /// getter completes before its caller continues, so nothing
            /// retains the frame.
            /// </summary>
            internal int EnterGetter(string memberId, object? receiver)
            {
                IReadOnlyCollection<string> stack = CallFrameStack.Push(
                    getterCallStack as IReadOnlyList<string> ?? getterCallStack.ToArray(), memberId);
                int frame = EnterThis(receiver);
                ref FunctionFrame entered = ref frameStack!.frames[frame];
                if (!ReferenceEquals(entered.getterCallStack, stack))
                    entered.getterCallStack = stack;
                getterCallStackFrame = frame;
                return frame;
            }

            /// <summary>
            /// Opens a construction frame on this context instead of a fork:
            /// a constructor cannot await, so nothing retains the frame once
            /// the construction returns.
            /// </summary>
            /// <param name="receiver">The frame's <c>this</c>.</param>
            internal int EnterConstruction(string label, object? receiver) =>
                EnterConstructionStack(CallFrameStack.Push(constructionStack, label), receiver);

            /// <param name="siteFrames">The construction site's frames, see <see cref="CallFrameStack.Push(IReadOnlyList{string}, string, ref CallFrameStack?[])"/>.</param>
            internal int EnterConstruction(string label, ref CallFrameStack?[]? siteFrames) =>
                EnterConstructionStack(CallFrameStack.Push(constructionStack, label, ref siteFrames), thisValue);

            private int EnterConstructionStack(IReadOnlyList<string> stack, object? receiver)
            {
                int frame = EnterThis(receiver);
                ref FunctionFrame entered = ref frameStack!.frames[frame];
                if (!ReferenceEquals(entered.constructionStack, stack))
                    entered.constructionStack = stack;
                constructionStackFrame = frame;
                return frame;
            }

        }

        /// <summary>
        /// Reference equality for visited collections and patched aliases
        /// on netstandard2.1.
        /// </summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // Keep the index with the shared reverse table, including contexts made
        // through the public constructor. Weak aliases do not keep script locals alive.
        private static readonly ConditionalWeakTable<ConditionalWeakTable<object, RowReference>, RowAliasIndex>
            RowAliasIndexes = new();

        // A method group converted at the call site allocates a delegate per
        // lookup, and the lookup runs on every row refresh.
        private static readonly ConditionalWeakTable<ConditionalWeakTable<object, RowReference>, RowAliasIndex>.CreateValueCallback
            CreateRowAliasIndexCallback = CreateRowAliasIndex;

        private static RowAliasIndex CreateRowAliasIndex(ConditionalWeakTable<object, RowReference> reverse)
        {
            var index = new RowAliasIndex();
            foreach (var pair in reverse)
                index.Add(pair.Key, pair.Value);
            return index;
        }

        /// <summary>
        /// Lists and dictionaries a context family looked up in its row
        /// reverse index, and lists in the detached-origin table, as of
        /// <see cref="CollectionAliasEpoch"/>: a weak-table lookup takes a
        /// lock and hashes the collection, and frames read the same ones
        /// again and again (an enum value is a shared one-entry list). A
        /// collection becomes an alias only when first unwrapped or exposed,
        /// so a miss stays a miss until the epoch moves. Direct-mapped by identity hash, which Mono's non-moving GC
        /// derives from the address without a call.
        /// </summary>
        private sealed class CollectionRowCache
        {
            private struct Entry
            {
                internal object? collection;
                internal RowReference? row;
                internal NeoGeneratedTypesSupport.DetachedArrayOrigin? detached;
                internal int epoch;
                internal bool detachedKnown;
            }

            private const int Capacity = 64;
            private const int IndexShift = 26;
            // Allocated on the first read: a family that reads no collection never pays for it.
            private Entry[]? entries;
            private ConditionalWeakTable<object, RowReference>? table;

            internal RowReference? Find(object collection, ConditionalWeakTable<object, RowReference> index) =>
                Slot(collection, index).row;

            internal void NoteFresh(object?[] array, ConditionalWeakTable<object, RowReference> index)
            {
                ref Entry entry = ref Entries(index)[IndexOf(array)];
                entry.collection = array;
                entry.row = null;
                entry.detached = null;
                entry.detachedKnown = true;
                entry.epoch = CollectionAliasEpoch;
            }

            internal NeoGeneratedTypesSupport.DetachedArrayOrigin? Detached(
                object?[] array,
                ConditionalWeakTable<object, RowReference> index)
            {
                ref Entry entry = ref Slot(array, index);
                if (!entry.detachedKnown)
                {
                    NeoGeneratedTypesSupport.TryGetDetachedArrayOrigin(array, out entry.detached);
                    entry.detachedKnown = true;
                }
                return entry.detached;
            }

            private ref Entry Slot(object collection, ConditionalWeakTable<object, RowReference> index)
            {
                int current = CollectionAliasEpoch;
                ref Entry entry = ref Entries(index)[IndexOf(collection)];
                if (entry.epoch != current || !ReferenceEquals(entry.collection, collection))
                {
                    index.TryGetValue(collection, out RowReference? row);
                    entry.collection = collection;
                    entry.row = row;
                    entry.detached = null;
                    entry.detachedKnown = false;
                    entry.epoch = current;
                }
                return ref entry;
            }

            private Entry[] Entries(ConditionalWeakTable<object, RowReference> index)
            {
                if (entries is null)
                {
                    entries = new Entry[Capacity];
                    table = index;
                }
                else if (!ReferenceEquals(table, index))
                {
                    Array.Clear(entries, 0, Capacity);
                    table = index;
                }
                return entries;
            }

            private static int IndexOf(object collection) =>
                (int)((uint)RuntimeHelpers.GetHashCode(collection) * 2654435769u >> IndexShift);
        }

        internal sealed class RowAliasIndex
        {
            private readonly Dictionary<RowKey, List<WeakReference<object>>> rows = new();

            internal void Add(object alias, RowReference row)
            {
                RowKey key = RowCacheRowKey(row.ownership, row.valueId);
                if (!rows.TryGetValue(key, out var aliases))
                    rows[key] = aliases = new();
                aliases.Add(new WeakReference<object>(alias));
            }

            internal void Remove(object alias, RowReference row)
            {
                RowKey key = RowCacheRowKey(row.ownership, row.valueId);
                if (!rows.TryGetValue(key, out var aliases))
                    return;
                for (int i = aliases.Count - 1; i >= 0; i--)
                    if (!aliases[i].TryGetTarget(out var target) || ReferenceEquals(target, alias))
                        aliases.RemoveAt(i);
                if (aliases.Count == 0)
                    rows.Remove(key);
            }

            internal IEnumerable<object> Get(NeoValueOwnership ownership, string id)
            {
                RowKey key = RowCacheRowKey(ownership, id);
                if (!rows.TryGetValue(key, out var aliases))
                    yield break;
                for (int i = aliases.Count - 1; i >= 0; i--)
                    if (aliases[i].TryGetTarget(out var target))
                        yield return target;
                    else
                        aliases.RemoveAt(i);
                if (aliases.Count == 0)
                    rows.Remove(key);
            }

            /// <summary><see cref="Get"/> without its enumerator, for the per-commit refresh.</summary>
            internal void GetInto(NeoValueOwnership ownership, string id, List<object> into)
            {
                RowKey key = RowCacheRowKey(ownership, id);
                if (!rows.TryGetValue(key, out var aliases))
                    return;
                for (int i = aliases.Count - 1; i >= 0; i--)
                    if (aliases[i].TryGetTarget(out var target))
                        into.Add(target);
                    else
                        aliases.RemoveAt(i);
                if (aliases.Count == 0)
                    rows.Remove(key);
            }

            internal void Remove(NeoValueOwnership ownership, string id) => rows.Remove(RowCacheRowKey(ownership, id));

            internal bool Contains(RowKey key) => rows.ContainsKey(key);
        }

        // Moves after any list or dictionary becomes or stops being a row
        // alias, or a list becomes a detached-slot alias, so a variable can
        // remember what its list aliased as of a count.
        private static int collectionAliasEpoch = 1;

        internal static int CollectionAliasEpoch => collectionAliasEpoch;

        internal static void NoteCollectionAlias() => collectionAliasEpoch++;

        private static void SetRowReference(Context ctx, object alias, RowReference row)
        {
            // Reads only need object-to-row lookup. Build the reverse alias lists
            // on the first write that must update existing CLR aliases.
            RowAliasIndex? aliases = ctx.BuiltRowAliases;
            if (aliases is not null && ctx.rowReverseIndex.TryGetValue(alias, out var previous))
                aliases.Remove(alias, previous);
            ctx.rowReverseIndex.Remove(alias);
            ctx.rowReverseIndex.Add(alias, row);
            if (alias is object?[] or IDictionary<string, object?>)
                NoteCollectionAlias();
            aliases?.Add(alias, row);
            if (alias is NeoObjectRecord record)
            {
                record.reference = row;
                record.referenceIndex = ctx.rowReverseIndex;
            }
        }

        // A just-materialized unwrap: no entry to replace, and no variable
        // could have remembered it as a plain list, so the epoch stays.
        private static void AddFreshRowReference(Context ctx, object alias, RowReference row)
        {
            ctx.rowReverseIndex.Add(alias, row);
            ctx.BuiltRowAliases?.Add(alias, row);
            if (alias is NeoObjectRecord record)
            {
                record.reference = row;
                record.referenceIndex = ctx.rowReverseIndex;
            }
        }

        private static void RemoveRowReference(Context ctx, object alias)
        {
            ctx.rowReverseIndex.Remove(alias);
            if (alias is object?[] or IDictionary<string, object?>)
                NoteCollectionAlias();
            if (alias is NeoObjectRecord record && ReferenceEquals(record.referenceIndex, ctx.rowReverseIndex))
            {
                record.reference = null;
                record.referenceIndex = null;
            }
        }

        /// <summary>Ownership-qualified row identity used as an allocation-free cache key.</summary>
        public readonly struct RowKey : IEquatable<RowKey>
        {
            internal readonly NeoValueOwnership ownership;
            // Hashed once, so a key that probes several tables hashes its id
            // once. It fills the struct's padding beside the ownership.
            private readonly int hash;
            internal readonly string rowId;
            internal RowKey(NeoValueOwnership ownership, string rowId)
            {
                this.ownership = ownership;
                this.rowId = rowId;
                hash = unchecked(rowId.GetHashCode() * 31 + (int)ownership);
            }
            public bool Equals(RowKey other) => ownership == other.ownership && SameId(rowId, other.rowId);
            public override bool Equals(object? obj) => obj is RowKey other && Equals(other);
            public override int GetHashCode() => hash;
        }

        /// <summary>Row identity plus the member it was unwrapped through.</summary>
        public readonly struct RowCacheKey : IEquatable<RowCacheKey>
        {
            internal readonly NeoValueOwnership ownership;
            internal readonly string rowId;
            internal readonly string? memberId;
            internal RowCacheKey(NeoValueOwnership ownership, string rowId, string? memberId)
            {
                this.ownership = ownership;
                this.rowId = rowId;
                this.memberId = memberId;
            }
            public bool Equals(RowCacheKey other) => ownership == other.ownership
                && SameId(rowId, other.rowId)
                && SameId(memberId, other.memberId);
            public override bool Equals(object? obj) => obj is RowCacheKey other && Equals(other);
            public override int GetHashCode() => unchecked((rowId.GetHashCode() * 31 + (int)ownership) * 31 + (memberId?.GetHashCode() ?? 0));
        }

        public sealed class RowReference
        {
            public string valueId
            {
                get;
            }
            public NeoValueOwnership ownership
            {
                get;
            }
            public string? classId
            {
                get;
            }
            internal JsonMember? member
            {
                get;
            }
            private bool collectionMembersResolved;
            private JsonMember? collectionMember;
            private JsonMember? entryMember;
            /// <summary>The row's value node, kept so repeated reads skip the id lookup.</summary>
            internal NeoValueNode? node;
            // The getter capture that last recorded a read of this row.
            internal long capturedIn;
            private NeoClassNode? classNode;
            // The class id instance classNode was last matched to: row class
            // ids are separate strings from the schema's, so reads of the same
            // row compare by reference instead of by content.
            private string? classNodeKey;

            /// <summary>The class node of <paramref name="classId"/>, kept across reads of this row.</summary>
            internal NeoClassNode ClassNode(NeoClient client, string classId)
            {
                if (classNode is { live: true } cached && ReferenceEquals(classNodeKey, classId))
                    return cached;
                if (classNode is not { live: true } current || !SameId(current.Id, classId))
                    classNode = client.ResolveClassNode(classId);
                classNodeKey = classId;
                return classNode;
            }

            public RowReference(
                string valueId,
                NeoValueOwnership ownership,
                string? classId = null,
                JsonMember? member = null)
            {
                this.valueId = valueId;
                this.ownership = ownership;
                this.classId = classId;
                this.member = member;
            }

            private struct EntrySlot
            {
                internal string id;
                internal NeoValueNode node;
            }

            private struct GetterSlot
            {
                internal string memberId;
                internal NeoValueOwnership readOwnership;
                internal NeoClient.GetterMemoEntry entry;
            }

            // The client's memo entries for getters read on this row, so a
            // repeated read skips hashing the memo key's two ids. A slot holds
            // while the client keeps its entry.
            private GetterSlot[]? getterSlots;
            private int getterSlotCount;
            private const int MaxGetterSlots = 8;

            internal NeoClient.GetterMemoEntry? MemoizedGetter(string memberId, NeoValueOwnership readOwnership)
            {
                for (int i = 0; i < getterSlotCount; i++)
                {
                    ref GetterSlot slot = ref getterSlots![i];
                    // A site passes the same id instance every time.
                    if (slot.readOwnership == readOwnership
                        && (ReferenceEquals(slot.memberId, memberId) || slot.memberId == memberId))
                        return slot.entry.forgotten ? null : slot.entry;
                }
                return null;
            }

            internal void RememberGetter(string memberId, NeoValueOwnership readOwnership, NeoClient.GetterMemoEntry entry)
            {
                for (int i = 0; i < getterSlotCount; i++)
                {
                    ref GetterSlot slot = ref getterSlots![i];
                    if (slot.readOwnership == readOwnership && string.Equals(slot.memberId, memberId))
                    {
                        slot.entry = entry;
                        return;
                    }
                }
                // Bounded: further getters take the memo's own lookup.
                if (getterSlotCount == MaxGetterSlots)
                    return;
                getterSlots ??= new GetterSlot[2];
                if (getterSlotCount == getterSlots.Length)
                    Array.Resize(ref getterSlots, getterSlotCount * 2);
                getterSlots[getterSlotCount++] = new GetterSlot
                {
                    memberId = memberId,
                    readOwnership = readOwnership,
                    entry = entry,
                };
            }

            // A collection row's entry nodes by position, so iterating it
            // again skips each entry's id lookup. A slot holds while the
            // position stores that exact id string and its node lives.
            private EntrySlot[]? entryNodes;

            internal NeoValueNode? EntryNode(int index, object? id) =>
                entryNodes is { } slots && index < slots.Length && ReferenceEquals(slots[index].id, id)
                    ? slots[index].node
                    : null;

            internal void RememberEntryNode(int index, int count, object? id, NeoValueNode? node)
            {
                if (id is not string entryId || node is not { live: true })
                    return;
                if (entryNodes is null || entryNodes.Length < count)
                    Array.Resize(ref entryNodes, count);
                ref EntrySlot slot = ref entryNodes[index];
                // A repeat read finds its own slot; rewriting it would only
                // pay two GC write barriers.
                if (ReferenceEquals(slot.node, node) && ReferenceEquals(slot.id, entryId))
                    return;
                slot.id = entryId;
                slot.node = node;
            }

            /// <summary>The row's member, inferred once when the read carried none.</summary>
            internal JsonMember? CollectionMember(NeoClient client)
            {
                ResolveCollectionMembers(client);
                return collectionMember;
            }

            /// <summary>
            /// The declared entry member of a List or Dictionary row, resolved
            /// on the first entry read. Authored String entries carry no
            /// localization mode, so only this member says an entry stores a
            /// text id; entry reads pass it as a member read passes its own.
            /// </summary>
            internal JsonMember? EntryMember(NeoClient client)
            {
                ResolveCollectionMembers(client);
                return entryMember;
            }

            private void ResolveCollectionMembers(NeoClient client)
            {
                if (collectionMembersResolved)
                    return;
                collectionMembersResolved = true;
                collectionMember = member;
                // A class row is never a collection; skip the parent walk.
                if (collectionMember is null && classId is null)
                    client.TryInferMemberForValueId(valueId, out collectionMember);
                if (collectionMember is ListMember or DictionaryMember
                    && client.TryGetValue(ownership, valueId, out MemberValue? row))
                    entryMember = client.TryResolveCollectionEntryMember(collectionMember, row);
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
        /// <param name="handlerFrame">
        /// The frame the body runs in, or -1 for a context of its own: the
        /// immediate handlers are installed there, as an in-place function
        /// call installs them, instead of on a fork of the context per
        /// evaluation. Null leaves the context's handlers alone.
        /// </param>
        internal static object? Evaluate(
            FunctionWithReturnType getter,
            Context ctx,
            object?[] argumentValues,
            int? handlerFrame = null)
        {
            NeoScriptScopeLayout layout = getter.scopeLayout ??= new NeoScriptScopeLayout(getter);
            var scope = layout.RentScope();
            bool completed = false;
            try
            {
                // Declared parameters bind by slot, without hashing their names.
                if (layout.thisSlot >= 0)
                    scope.SetParameter(layout.thisSlot, ctx.thisValue);
                else
                    scope["__this__"] = ctx.thisValue;
                if (layout.rootSlot >= 0)
                    scope.SetParameter(layout.rootSlot, ctx.rootValue);
                else
                    scope["__root__"] = ctx.rootValue;
                // Only a dialogue body declares __context__. Any other body
                // gets it as a dynamic binding when there is one, as a
                // setter's does.
                if (layout.contextSlot >= 0)
                    scope.SetParameter(layout.contextSlot, ctx.contextValue);
                else if (ctx.contextValue is not null)
                    scope["__context__"] = ctx.contextValue;
                if (argumentValues.Length > 0
                    && (getter.parameters?.Length ?? 0) != argumentValues.Length + 2)
                {
                    throw new NSGetterRuntimeError(
                        $"Initializer declares {Math.Max(0, (getter.parameters?.Length ?? 0) - 2)} constructor parameter(s), but received {argumentValues.Length} value(s).");
                }
                for (int i = 0; i < argumentValues.Length; i++)
                {
                    scope.SetParameter(i + 2, argumentValues[i]);
                }
                // Getters, actions, setters, and NSFunctions now share the same
                // effect-capable executor. Writability is a compile/runtime target
                // property, not a reason to maintain a second pure interpreter.
                // Immediate options let every getter frame share the client's
                // prebuilt expression handlers instead of closing over its own.
                NeoScriptExecutionOptions options = NeoScriptExecutionOptions.ForImmediate(ctx.client);
                if (handlerFrame is int frame)
                    NeoScriptExecutor.PrepareFunctionContext(ctx, options, frame);
                NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                    ctx.client,
                    getter,
                    scope,
                    ctx,
                    options);
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
                    completed = true;
                    return result.ReturnValue;
                }
                throw new NSGetterRuntimeError("Function ended without a return statement");
            }
            finally
            {
                if (completed)
                    layout.ReturnScope(scope);
                else
                    layout.AbandonScope(scope);
            }
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

        internal static object? UnwrapRow(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership,
            JsonMember member) =>
            UnwrapCached(row, ctx, ownership, member);

        internal static object? UnwrapRow(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership,
            NeoValueNode? node) =>
            UnwrapCached(row, ctx, ownership, node: node);

        /// <summary>Whether <paramref name="value"/> is a record this context unwrapped from a row.</summary>
        internal static bool IsRowRecord(object? value, Context ctx) =>
            value is NeoObjectRecord { reference: not null } record
                && ReferenceEquals(record.referenceIndex, ctx.rowReverseIndex);

        /// <summary>The node of row <paramref name="valueId"/> when <paramref name="value"/> is a record unwrapped from it.</summary>
        internal static NeoValueNode? RecordNode(object? value, string valueId, Context ctx) =>
            value is NeoObjectRecord { reference: { } rowRef } record
                && ReferenceEquals(record.referenceIndex, ctx.rowReverseIndex)
                && SameId(rowRef.valueId, valueId)
                ? rowRef.node
                : null;

        internal static object? EvaluatePointer(
            Pointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            return EvalPointer(pointer, new NeoScriptScope(scope), ctx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static object? EvaluatePointer(
            Pointer pointer,
            NeoScriptScope scope,
            Context ctx)
        {
            return EvalPointer(pointer, scope, ctx);
        }

        // ---------------------------------------------------------------
        // Pointers — 14 kinds
        // ---------------------------------------------------------------

        internal static object? EvalPointer(
            Pointer pointer,
            NeoScriptScope scope,
            Context ctx)
        {
            // Every case body with locals of its own lives in a helper: this
            // dispatcher runs for every expression, and a case's locals or
            // exception region would widen the frame each call sets up.
            switch (pointer)
            {
                case ValuePointer vp:
                    return vp.primitiveResolved ? vp.primitive : EvalValueLiteral(vp, ctx);
                case VariablePointer vrp:
                    return EvalVariable(vrp, scope, ctx);
                case ReferencePointer rp:
                    return EvalReference(rp, ctx);
                // Hot kinds first: each case is a type test.
                case CallFunctionPointer functionCall:
                    return EvalFunctionCall(functionCall, scope, ctx);
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
                case VariantPointer variantPointer:
                    return EvalVariant(variantPointer);
                case StaticMemberPointer staticPointer:
                    return EvalStaticMember(staticPointer, ctx);
                case ListLiteralPointer llp:
                    return EvalListLiteral(llp, scope, ctx);
                case DictLiteralPointer dlp:
                    return EvalDictLiteral(dlp, scope, ctx);
                case ForceUnwrapPointer fup:
                    return EvalForceUnwrap(fup, scope, ctx);
                case IsCheckPointer icp:
                    {
                        var v = EvalPointer(icp.pointer, scope, ctx);
                        return Box(RuntimeTypeCheck(v, icp.checkType, ctx));
                    }
                case CallGetterPointer cgp:
                    return EvalCallGetter(cgp, scope, ctx);
                case MemberTargetPointer target:
                    {
                        string? valueId = null;
                        if (!target.receiver.IsStatic)
                        {
                            object? receiver = EvalCallReceiver(target.receiver, scope, ctx);
                            valueId = FindRowIdByReference(receiver, ctx)
                                ?? throw new NSGetterRuntimeError("A listener receiver must have a logical value identity.");
                        }
                        return new NeoDelegateValue { memberId = target.memberId, valueId = valueId };
                    }
                case CoalescePointer cp:
                    {
                        var left = EvalPointer(cp.left, scope, ctx);
                        if (left is not null)
                            return left;
                        return EvalPointer(cp.right, scope, ctx);
                    }
                case ObjectInitializerPointer initializer:
                    return ctx.expressionHandlers is { } handlers
                        ? handlers.Initialize(initializer, scope, ctx)
                        : NeoScriptExecutor.EvaluateImmediateObjectInitializer(initializer, scope, ctx);
                case ConditionalPointer conditional:
                    {
                        var condition = EvalPointer(conditional.condition, scope, ctx);
                        return EvalPointer(
                            JsTruthy(condition)
                                ? conditional.whenTrue
                                : conditional.whenFalse,
                            scope,
                            ctx);
                    }
                case DelegateClosurePointer closurePointer:
                    return EvalDelegateClosure(closurePointer, scope, ctx);
                case ToBoolPointer tbp:
                    {
                        var v = EvalPointer(tbp.pointer, scope, ctx);
                        return Box(JsTruthy(v));
                    }
                case StringifyPointer sp:
                    {
                        var v = EvalPointer(sp.pointer, scope, ctx);
                        string result = FormatForInterp(v, sp.sourceType, ctx);
                        return result;
                    }
                case TileConvertPointer convert:
                    return EvalTileConvert(convert, scope, ctx);
                case CallDelegatePointer delegateCall:
                    return EvalDelegateCall(delegateCall, scope, ctx);
                case CallActionPointer actionCall:
                    // An NSAction is void by construction; the enclosing
                    // functionCall instruction discards this (P62 §3.1).
                    EvalCallAction(actionCall, scope, ctx);
                    return null;
                case FunctionErrorCheckPointer functionErrorCheck:
                    return EvalFunctionErrorCheck(functionErrorCheck, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown pointer kind {pointer.GetType().Name}");
            }
        }

        /// <summary>A literal that isn't a converted primitive.</summary>
        private static object? EvalValueLiteral(ValuePointer vp, Context ctx)
        {
            if (vp.primitiveEntries is object?[] template)
            {
                // Nothing writes an enum value in place: a slot keeps a bare
                // id or its own copy, a row its own strings, and a local list
                // mutation copies. So every evaluation shares one array.
                if (vp.value.typeInfo is EnumTypeInfo)
                {
                    ctx.NoteFreshList(template);
                    return template;
                }
                object?[] copy = CopyEntries(template);
                ctx.NoteFreshList(copy);
                return copy;
            }
            switch (vp.valueTemplate)
            {
                case NeoDelegateValue delegateTemplate:
                    return BindDelegateLiteral(delegateTemplate.PersistedCopy(), ctx);
                case NeoActionValue actionTemplate:
                    return actionTemplate.PersistedCopy();
            }
            if (NeoDelegateValueConverter.LooksLikeValue(vp.value.value))
            {
                vp.valueTemplate = vp.value.value!.ToObject<NeoDelegateValue>()!;
                return EvalValueLiteral(vp, ctx);
            }
            // Clear() lowers to an action literal assignment. Preserve
            // its listener-set type instead of unwrapping it as a map.
            if (vp.value.typeInfo.type == MemberKind.NSAction)
            {
                vp.valueTemplate = vp.value.value?.ToObject<NeoActionValue>() ?? new NeoActionValue();
                return EvalValueLiteral(vp, ctx);
            }
            JToken? literal = vp.value.value;
            if (literal is null
                || literal.Type is JTokenType.Null
                    or JTokenType.Undefined
                    or JTokenType.Boolean
                    or JTokenType.Integer
                    or JTokenType.Float
                    or JTokenType.String)
            {
                // Primitive literals unwrap to immutable CLR values:
                // convert the token once, not on every evaluation.
                vp.primitive = UnwrapJToken(literal);
                vp.primitiveIsNumber = TryAsDouble(vp.primitive, out vp.primitiveNumber);
                vp.primitiveResolved = true;
                return vp.primitive;
            }
            // An array literal of primitives converts once; see above for
            // which evaluations share it.
            if (literal is Newtonsoft.Json.Linq.JArray entries && IsPrimitiveArray(entries))
            {
                vp.primitiveEntries = (object?[])UnwrapJToken(literal)!;
                return EvalValueLiteral(vp, ctx);
            }
            return UnwrapJToken(literal);
        }

        private static NeoDelegateValue BindDelegateLiteral(NeoDelegateValue value, Context ctx)
        {
            if (value.IsClosure)
            {
                ctx.allocationTracker.ReusableContext = false;
                return value.Capture(ctx.thisValue, ctx.rootValue);
            }

            // Bind implicit and explicit this.Member literals at creation,
            // as the web evaluator does. Invocation may have a different this.
            if (value.valueId is null
                && ctx.client.TryGetMember(value.memberId!, out JsonMember? member)
                && member.Modifier != NeoMemberModifierKind.Static)
            {
                value.valueId = FindRowIdByReference(ctx.thisValue, ctx);
            }
            return value;
        }

        /// <summary>A variable read, following a row-backed or attached alias.</summary>
        private static object? EvalVariable(VariablePointer vrp, NeoScriptScope scope, Context ctx)
        {
            RowReference? listRef = null;
            var v = scope.ReadVariable(vrp, ctx.rowReverseIndex, out bool found, out bool remembered, ref listRef);
            if (!found)
            {
                throw new NSGetterRuntimeError(
                    $"Variable '{vrp.variableId}' is not in scope");
            }
            // Row-backed list aliases retain provenance even when a
            // mutation replaces their fixed-size CLR array.
            if (v is object?[] entries)
            {
                if (remembered && listRef is null)
                    return v;
                int epoch = CollectionAliasEpoch;
                if (!remembered && ctx.CollectionRowReference(entries) is { } indexed)
                {
                    listRef = indexed;
                    scope.RememberListAlias(vrp, entries, ctx.rowReverseIndex, epoch, listRef);
                }
                if (listRef is not null
                    && ctx.client.ReadValue(listRef.ownership, listRef.valueId, ref listRef.node) is ArrayMemberValue listRow)
                {
                    return UnwrapCached(listRow, ctx, listRef.ownership, listRef.member, listRef.node);
                }
                if (ctx.ArrayDetachedOrigin(entries) is { } detachedArray)
                    return ReadDetachedArrayAlias(detachedArray, ctx);
                if (listRef is null)
                    scope.RememberListAlias(vrp, entries, ctx.rowReverseIndex, epoch, null);
            }
            else if (v is NeoScriptObject { attachedId: not null } attached)
            {
                return ForwardDetached(attached, ctx);
            }
            return v;
        }

        /// <summary>A value reference's row.</summary>
        private static object? EvalReference(ReferencePointer rp, Context ctx)
        {
            NeoValueNodeSite? site = rp.valueNode;
            if (site is null || !ReferenceEquals(site.schemaResolution, ctx.client.SchemaResolution))
            {
                rp.valueNode = site = new NeoValueNodeSite(ctx.client.SchemaResolution);
                ctx.client.RememberSchemaResolutionSite(rp);
            }
            var ownership = ResolveOwnershipForValueId(ctx, rp.valueId, ref site.node);
            NeoValueNode? node = site.node;
            MemberValue? row = null;
            if (rp.withProvenance == true
                && FindRowIdByReference(ctx.thisValue, ctx) is string receiverId)
            {
                NeoValueOwnership receiverOwnership =
                    FindRowOwnershipByReference(ctx.thisValue, ctx)
                    ?? ctx.valueOwnership;
                try
                {
                    if (ctx.client.TryResolveProvenanceReference(
                            receiverOwnership,
                            receiverId,
                            rp.valueId,
                            out MemberValue? provenanceRow,
                            out NeoValueOwnership provenanceOwnership))
                    {
                        row = provenanceRow;
                        ownership = provenanceOwnership;
                        node = null;
                    }
                }
                catch (InvalidOperationException error)
                {
                    throw new NSGetterRuntimeError(error.Message);
                }
            }
            row ??= ctx.client.ReadReplayReference(rp.valueId, ref node, ownership);
            if (row is null)
            {
                throw new NSGetterRuntimeError(
                    $"Missing value reference: {rp.valueId}");
            }
            return UnwrapCached(row, ctx, ownership, node: node);
        }

        /// <summary>A variant reference pair.</summary>
        private static object? EvalVariant(VariantPointer variantPointer)
        {
            // P67 §6. The pair is the value; resolution to a record
            // happens in the two intrinsics that consume it, so an
            // unused variant reference costs nothing.
            if (string.IsNullOrEmpty(variantPointer.classId))
            {
                throw new NSGetterRuntimeError(
                    "Variant reference carries no classId.");
            }
            return new NeoVariantReference(
                variantPointer.classId,
                variantPointer.variantId,
                variantPointer.rowValueId);
        }

        /// <summary>A static member's bound value.</summary>
        private static object? EvalStaticMember(StaticMemberPointer staticPointer, Context ctx)
        {
            if (!ctx.client.TryGetMember(
                    staticPointer.memberId,
                    out JsonMember? staticMember)
                || staticMember.Modifier != NeoMemberModifierKind.Static)
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

        /// <summary>A list literal's entries.</summary>
        private static object? EvalListLiteral(ListLiteralPointer llp, NeoScriptScope scope, Context ctx)
        {
            var arr = new object?[llp.entries.Length];
            for (int i = 0; i < llp.entries.Length; i++)
            {
                arr[i] = EvalPointer(llp.entries[i], scope, ctx);
            }
            return arr;
        }

        /// <summary>A dictionary literal's entries.</summary>
        private static object? EvalDictLiteral(DictLiteralPointer dlp, NeoScriptScope scope, Context ctx)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var entry in dlp.entries)
            {
                var k = EvalPointer(entry.key, scope, ctx);
                dict[k?.ToString() ?? "null"] = EvalPointer(entry.value, scope, ctx);
            }
            return dict;
        }

        /// <summary>A force-unwrapped value, which must not be null.</summary>
        private static object? EvalForceUnwrap(ForceUnwrapPointer fup, NeoScriptScope scope, Context ctx)
        {
            var v = EvalPointer(fup.pointer, scope, ctx);
            if (v is null)
            {
                throw new NSGetterRuntimeError(
                    $"Unexpectedly found null while force-unwrapping a value (unwrapped pointer kind: {DescribePointer(fup.pointer)})");
            }
            return v;
        }

        /// <summary>A getter call, dispatched on the receiver's runtime Class.</summary>
        private static object? EvalCallGetter(CallGetterPointer cgp, NeoScriptScope scope, Context ctx)
        {
            // Most sites dispatch virtually: a null check skips the compare.
            bool baseDispatch = cgp.dispatch is not null && cgp.dispatch == "base";
            bool isStatic = cgp.receiver.IsStatic;
            if (baseDispatch && isStatic)
                throw new NSGetterRuntimeError("Base dispatch requires an instance receiver.");
            if (isStatic)
            {
                JsonMember? member = ResolveGetterMember(cgp, ctx);
                ValidateStaticCallableReceiver(
                    cgp.receiver,
                    cgp.memberId,
                    "getter",
                    member);
                return DispatchNSGetterById(
                    cgp.memberId,
                    receiver: null,
                    ctx,
                    (member as NSPropertyMember)?.getter);
            }
            var innerThis = EvalCallReceiver(cgp.receiver, scope, ctx);
            if (cgp.optional == true && innerThis is null)
                return null;
            if (baseDispatch)
                return DispatchNSGetterById(cgp.memberId, innerThis, ctx, (ResolveGetterMember(cgp, ctx) as NSPropertyMember)?.getter);
            // Try runtime dispatch via the receiver's classId merged
            // schema first — same trick the TS evaluator uses to
            // honor runtime overrides regardless of the static
            // compile-time binding.
            SchemaPlacement? placement = FindSchemaPlacementCached(cgp, ctx);
            if (placement is not null)
            {
                object? dispatched = DispatchSchemaMember(innerThis, placement.schemaKey, ctx, cgp);
                if (Dispatched(dispatched))
                    return dispatched;
            }
            return DispatchNSGetterById(cgp.memberId, innerThis, ctx, (ResolveGetterMember(cgp, ctx) as NSPropertyMember)?.getter);
        }

        /// <summary>
        /// A getter call site's member, resolved once per schema resolution
        /// rather than looked up by id on every read.
        /// </summary>
        private static JsonMember? ResolveGetterMember(CallGetterPointer site, Context ctx)
        {
            NeoClient client = ctx.client;
            GetterMemberSite? cached = site.getterMemberSite;
            if (cached is not null && ReferenceEquals(cached.schemaResolution, client.SchemaResolution))
                return cached.member;
            client.TryGetMember(site.memberId, out JsonMember? member);
            site.getterMemberSite = new GetterMemberSite(client.SchemaResolution, member);
            client.RememberSchemaResolutionSite(site);
            return member;
        }

        /// <summary>
        /// The member a setter write through <paramref name="site"/> runs:
        /// on an instance receiver, the receiver Class's member at the site's
        /// schema placement, else the site's own. Resolved through the site's
        /// caches, as a dispatched read is.
        /// </summary>
        internal static string ResolveSetterMember(
            CallGetterPointer site,
            object? receiver,
            Context ctx,
            out JsonMember? member)
        {
            if (receiver is not null
                && FindSchemaPlacementCached(site, ctx) is { } placement
                && FindRowClassIdByReference(receiver, ctx) is { Length: > 0 } classId)
            {
                if (SurfaceRowMember(FindRowReference(receiver, ctx), classId, placement.schemaKey, site, ctx) is { } entry)
                {
                    // Only a variant target member needs the client's lookup.
                    member = entry.member;
                    if (member is null)
                        ctx.client.TryGetMember(entry.memberId, out member);
                    return entry.memberId;
                }
            }
            member = ResolveGetterMember(site, ctx);
            return site.memberId;
        }

        /// <summary>A getter call site's member under one schema resolution.</summary>
        internal sealed class GetterMemberSite
        {
            internal readonly object schemaResolution;
            internal readonly JsonMember? member;

            internal GetterMemberSite(object schemaResolution, JsonMember? member)
            {
                this.schemaResolution = schemaResolution;
                this.member = member;
            }
        }

        /// <summary>A delegate closure over its evaluated captures.</summary>
        private static object? EvalDelegateClosure(DelegateClosurePointer closurePointer, NeoScriptScope scope, Context ctx)
        {
            ctx.allocationTracker.ReusableContext = false;
            Pointer[] capturePointers =
                closurePointer.captures ?? Array.Empty<Pointer>();
            var captures = new object?[capturePointers.Length];
            for (int i = 0; i < captures.Length; i++)
            {
                captures[i] = EvalPointer(
                    capturePointers[i],
                    scope,
                    ctx);
            }
            return new NeoDelegateValue
            {
                code = closurePointer.code,
                action = closurePointer.action,
                captures = captures,
            }.Capture(ctx.thisValue, ctx.rootValue);
        }

        /// <summary>A delegate invocation.</summary>
        private static object? EvalDelegateCall(CallDelegatePointer delegateCall, NeoScriptScope scope, Context ctx)
        {
            object? callable = EvalPointer(delegateCall.@delegate, scope, ctx);
            if (callable is null)
            {
                if (delegateCall.optional == true)
                    return null;
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
                case VariantPointer variant:
                    return $"variant {variant.variantId ?? "Base"} of {variant.classId}";
                case CallFunctionPointer functionCall:
                    return $"functionCall {functionCall.memberId ?? functionCall.memberKey}";
                case CallDelegatePointer:
                    return "delegateCall";
                case CallActionPointer:
                    return "actionCall";
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

        /// <param name="returnType">The Function's declared return type, which shapes its result.</param>
        /// <param name="function">The call site's resolved signature, or null to resolve it per call.</param>
        /// <param name="ownsArguments">Whether <paramref name="args"/> is a call site's rented buffer.</param>
        internal static object? InvokeNativeFunction(string memberId, NeoClient.ResolvedNativeFunction? function,
            TypeInfo? returnType, object? receiver, object?[] args, Context ctx, bool ownsArguments = false)
        {
            if (function is null || function.intrinsic)
            {
                if (NeoCellPatternRuntime.TryInvoke(memberId, receiver, args, ctx, out object? result))
                    return result;
                if (ctx.client.ScriptGridQueries.TryInvoke(memberId, receiver, args, ctx, out result))
                    return result;
            }
            args = ResolveHostArguments(args, ctx, ref ownsArguments);
            object? value = function is null
                ? ctx.client.InvokeNativeFunction(memberId, receiver, args, ownsArguments)
                : ctx.client.InvokeNativeFunction(function, receiver, args, ownsArguments);
            return NeoCellPatternStorage.NormalizeNativeResult(value, ctx, returnType);
        }

        /// <summary>
        /// Evaluates one call argument. A pattern-producing argument stays as
        /// offsets: a grid query or an NSFunction parameter that only queries
        /// the grid reads it as is, and <see cref="MaterializePatternArguments"/>
        /// constructs it for any other target.
        /// </summary>
        internal static object? EvaluateFunctionArgument(CallFunctionPointer call, int index,
            NeoScriptScope scope, Context ctx)
        {
            if (call.args[index] is not CallFunctionPointer patternCall
                || !NeoCellPatternRuntime.ProducesPattern(patternCall.memberId))
                return EvalPointer(call.args[index], scope, ctx);

            var receiver = patternCall.receiver.pointer is CallGetterPointer { receiver: { IsStatic: true } } patternGetter
                ? EvalStaticPatternGetter(patternGetter, scope, ctx)
                : EvalCallReceiver(patternCall.receiver, scope, ctx);
            if (patternCall.optional == true && receiver is null && !patternCall.receiver.IsStatic)
                return null;
            object?[] args = RentArguments(patternCall);
            try
            {
                for (int i = 0; i < args.Length; i++)
                    args[i] = EvalPointer(patternCall.args[i], scope, ctx);
                CallSiteTarget? target = ResolveCallTarget(patternCall, receiver, ctx);
                if (target?.native is null || target.memberId != patternCall.memberId)
                    throw new NSGetterRuntimeError($"CellPattern intrinsic '{patternCall.memberId}' has an invalid native declaration.");
                NeoCellPatternRuntime.TryInvoke(target.memberId, receiver,
                    FillNativeCallSiteArguments(target.memberId, target.nativeFunction?.signature, args), ctx, out var result, materialize: false);
                return result;
            }
            finally
            {
                ReturnArguments(patternCall, args);
            }
        }

        /// <summary>
        /// A static getter read as the receiver of a pattern argument, like
        /// <c>NeoCellPattern.Center</c> in <c>GetTile(NeoCellPattern.Center.Translate(cell))</c>.
        /// Only the native call reads it, so a pattern result is kept as its
        /// offsets, memoized like an instance getter's result: a constructed
        /// pattern never escapes, and later reads skip constructing it.
        /// </summary>
        private static object? EvalStaticPatternGetter(CallGetterPointer getter, NeoScriptScope scope, Context ctx)
        {
            NeoClient client = ctx.client;
            if (!client.CanMemoizeGetters || getter.dispatch == "base")
                return EvalCallGetter(getter, scope, ctx);
            ValidateStaticCallableReceiver(getter.receiver, getter.memberId, "getter", ResolveGetterMember(getter, ctx));
            var key = new NeoClient.GetterMemoKey(
                ctx.valueOwnership, NeoClient.StaticGetterRowId, getter.memberId, ctx.valueOwnership);
            if (client.FindMemoizedGetter(key) is { } hit)
            {
                client.ReplayGetterReads(hit);
                return hit.scalar;
            }
            NeoClient.GetterCaptureFrame enclosingCapture = client.BeginGetterReadCapture();
            object? result;
            NeoClient.GetterCaptureFrame capture;
            try
            {
                result = EvalCallGetter(getter, scope, ctx);
            }
            finally
            {
                capture = client.EndGetterReadCapture(enclosingCapture);
            }
            if (result is NeoScriptObject { attachedId: null } detached
                && detached.plan.classId == NeoCellPatternStorage.ClassId)
            {
                NeoCellPattern pattern = NeoCellPatternStorage.ReadRuntime(detached, ctx);
                client.MemoizeGetter(key, pattern, null, capture);
                return pattern;
            }
            client.RecycleGetterCapture(capture);
            return result;
        }

        /// <summary>Constructs the pattern arguments left as offsets, unless the native target is a grid query.</summary>
        internal static void MaterializePatternArguments(string? memberId, object?[] args, Context ctx)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is not NeoCellPattern pattern)
                    continue;
                // Only a pattern asks which member reads it, which costs
                // id comparisons; a grid query reads every one as offsets.
                if (NeoScriptGridQueries.ReadsCells(memberId))
                    return;
                args[i] = NeoCellPatternStorage.Materialize(pattern, ctx);
            }
        }

        internal static object? NormalizeNativeResult(string memberId, object? value, Context ctx)
        {
            ctx.client.TryGetMember(memberId, out FunctionMember? member);
            return NeoCellPatternStorage.NormalizeNativeResult(value, ctx, member?.returnTypeInfo);
        }

        private static object? EvalFunctionCall(
            CallFunctionPointer pointer,
            NeoScriptScope scope,
            Context ctx)
        {
            if (ctx.expressionHandlers is { } handlers)
            {
                return handlers.Call(pointer, scope, ctx);
            }
            var receiver = EvalCallReceiver(pointer.receiver, scope, ctx);
            if (pointer.optional == true && receiver is null)
            {
                if (!pointer.receiver.IsStatic)
                    return null;
            }
            CallSiteTarget? target = ResolveCallTarget(pointer, receiver, ctx);
            if (target?.function is { } resolved)
            {
                return NeoNSFunctionRuntime.InvokeImmediate(
                    ctx.client,
                    resolved,
                    receiver,
                    Array.Empty<object?>(),
                    ctx,
                    site: pointer,
                    siteScope: scope);
            }
            object?[] args = RentArguments(pointer);
            try
            {
                for (int i = 0; i < pointer.args.Length; i++)
                {
                    args[i] = EvaluateFunctionArgument(pointer, i, scope, ctx);
                }
                MaterializePatternArguments(target?.memberId, args, ctx);
                if (target is null)
                {
                    return EvaluateMissingMemberFallback(pointer, receiver, args);
                }
                if (target.native is not null)
                {
                    return InvokeNativeFunction(target.memberId, target.nativeFunction, target.native.returnTypeInfo, receiver,
                        FillNativeCallSiteArguments(target.memberId, target.nativeFunction?.signature, args), ctx,
                        ownsArguments: true);
                }
                if (!ctx.client.TryGetMember(target.memberId, out JsonMember? _))
                {
                    throw new NSGetterRuntimeError(
                        $"Function member '{target.memberId}' was not found.");
                }
                throw new NSGetterRuntimeError(
                    $"Member '{target.memberId}' is not a callable Function member.");
            }
            finally
            {
                ReturnArguments(pointer, args);
            }
        }

        /// <summary>
        /// A call site's argument buffer, sized to its arguments. The site
        /// owns one buffer: a reentrant or concurrent call through the same
        /// site finds it in use and allocates its own. Arguments never
        /// outlive the call — callees copy what they keep. The buffer stays
        /// on the site and a flag tracks its use, so a call stores no
        /// reference into the site (Mono write-barriers each one).
        /// </summary>
        internal static object?[] RentArguments(CallFunctionPointer call)
        {
            if (call.args.Length == 0)
                return Array.Empty<object?>();
            if (call.argumentBufferInUse)
                return new object?[call.args.Length];
            call.argumentBufferInUse = true;
            return call.argumentBuffer ??= new object?[call.args.Length];
        }

        internal static void ReturnArguments(CallFunctionPointer call, object?[] args)
        {
            // A reentrant call's own array is garbage once it returns.
            if (!ReferenceEquals(args, call.argumentBuffer))
                return;
            NeoArgumentArrays.Clear(args);
            call.argumentBufferInUse = false;
        }

        /// <summary>
        /// P65 §2.5 for native call sites: the fill happens in the evaluator
        /// BEFORE dispatch, so <c>PrepareNativeFunctionInvocation</c> always
        /// receives full arity and its exact-match check stands. A member
        /// whose effective signature cannot resolve passes through untouched —
        /// the existing broken-signature error keeps firing inside dispatch,
        /// exactly as before. NSFunction calls need no twin here: their fill
        /// is callee-side in <c>NeoNSFunctionRuntime.ExecuteResolved</c>.
        /// Internal because the linked-frame executor's twin dispatch
        /// (<c>NeoScriptExecutor.EvalFunctionCall</c>) fills through the same
        /// seam.
        /// </summary>
        internal static object?[] FillNativeCallSiteArguments(
            string memberId,
            object?[] args,
            Context ctx)
        {
            ctx.client.TryResolveFunctionMember(memberId, out FunctionMember? signature);
            return FillNativeCallSiteArguments(memberId, signature, args);
        }

        /// <summary>
        /// <see cref="FillNativeCallSiteArguments(string, object?[], Context)"/>
        /// for a call site that already resolved its signature, or null when
        /// it has none.
        /// </summary>
        internal static object?[] FillNativeCallSiteArguments(
            string memberId,
            FunctionMember? signature,
            object?[] args)
        {
            if (signature is null)
                return args;
            // A full call has nothing to fill.
            if (args.Length == signature.argumentTypes.Length
                || !NeoParameterDefaults.HasAnyDefault(signature.argumentTypes))
            {
                return args;
            }
            return NeoParameterDefaults.FillTrailingDefaults(
                args,
                signature.argumentTypes,
                $"Function '{signature.name}' ({memberId})");
        }

        /// <summary>
        /// Invokes a closure or bound callable-member target. Public so
        /// generated SDK surfaces and animation runtime code use the same
        /// dispatch as the <c>callDelegate</c> IR pointer.
        /// </summary>
        public static object? InvokeDelegate(
            object value,
            object?[] args,
            Context ctx,
            object? ownerReceiver = null,
            NeoValueOwnership? receiverOwnership = null)
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
            return InvokeDelegateMemberTarget(delegateValue, args, ctx, ownerReceiver, receiverOwnership);
        }

        /// <summary>
        /// Fires every listener of an NSAction value in stored order
        /// (P62 §3.1). Public so generated SDK surfaces invoke actions
        /// through the same dispatch as the <c>callAction</c> IR pointer.
        /// An empty — or absent — listener set is a successful no-op, never
        /// the null-target throw a delegate raises; a throwing listener stops
        /// the invocation fail-fast with its frame named in the error.
        /// </summary>
        /// <param name="ownerReceiver">
        /// The row this action was read off, when the caller can name it. A
        /// listener stored with a null <c>valueId</c> runs against it
        /// (P62 §3.3); pass null and those listeners stay receiver-less.
        /// </param>
        /// <param name="actionFrame">
        /// The <c>{actionMemberName}[{owningRowId ?? "default"}]</c> frame a
        /// failing listener is reported under. Built lazily and identically
        /// on both runtimes: naming the owning row is wasted work — and an
        /// extra chance to throw — on the path where nothing fails.
        /// </param>
        public static void InvokeAction(
            object? value,
            object?[] args,
            Context ctx,
            object? ownerReceiver = null,
            Func<string>? actionFrame = null) =>
            InvokeListeners(value, args, ctx, ownerReceiver, actionFrame);

        /// <param name="frame">
        /// Names the failing frame only when a listener fails: the action
        /// <see cref="Pointer"/> an IR call read the action through (named
        /// against <paramref name="ownerReceiver"/>), a caller's
        /// <see cref="Func{TResult}"/>, or null for the unnamed fallback. A
        /// pointer needs no closure, so the IR call allocates nothing for it.
        /// </param>
        private static void InvokeListeners(
            object? value,
            object?[]? args,
            Context ctx,
            object? ownerReceiver,
            object? frame)
        {
            NeoActionValue? actionValue = CoerceActionValue(value);
            if (actionValue is null)
                return;
            args ??= Array.Empty<object?>();
            for (int index = 0; index < actionValue.listeners.Count; index++)
            {
                NeoDelegateValue listener = actionValue.listeners[index];
                if (listener is null || !listener.IsMemberTarget)
                {
                    throw new NSGetterRuntimeError(
                        $"{ActionFrameName(frame, ownerReceiver, ctx)} listener {index} is not a member target; only member targets are valid listeners.");
                }
                try
                {
                    InvokeDelegateMemberTarget(
                        listener,
                        args,
                        ctx,
                        ownerReceiver);
                }
                catch (Exception error)
                    when (NeoScriptErrorClassification.IsRewrappable(error))
                {
                    // Fail-fast: the remaining listeners do not run, and the
                    // error names the frame that failed. A nested action
                    // fan-out wraps again, so the message spells the whole
                    // listener path (P62 §3.1).
                    //
                    // Only an ordinary authored-catchable error is renamed. A
                    // pre-execution validation failure, a native-unavailable
                    // fault and a deferred-Function fault propagate as
                    // themselves, so the host handling that keys on their
                    // class still applies.
                    throw new NSGetterRuntimeError(
                        $"{ActionFrameName(frame, ownerReceiver, ctx)} listener {index} threw: {error.Message}");
                }
            }
        }

        /// <summary>
        /// The frame a failing listener is reported under. A caller that
        /// cannot name the member or the owning row gets the same fallback
        /// label the TS evaluator uses for a non-member action pointer.
        /// </summary>
        private static string ActionFrameName(
            object? frame,
            object? owner,
            Context ctx) => frame switch
            {
                Pointer pointer => ActionInvocationFrame(pointer, owner, ctx),
                Func<string> describe => describe(),
                _ => "action[default]",
            };

        /// <summary>
        /// Normalizes an evaluated NSAction value. A missing value is null:
        /// the rest state — the empty listener set — because an action is
        /// never nullable (P62 §2.1), and firing it does nothing.
        /// </summary>
        private static NeoActionValue? CoerceActionValue(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case NeoActionValue typed:
                    return typed;
                case JObject json:
                    return json.ToObject<NeoActionValue>();
                default:
                    throw new NSGetterRuntimeError(
                        $"NSAction value is not a listener set; received {value.GetType().Name}.");
            }
        }

        private static object? InvokeDelegateClosure(
            NeoDelegateValue value,
            object?[] args,
            Context ctx)
        {
            FunctionWithReturnType action = value.action
                ?? throw new NSGetterRuntimeError(
                    "NeoDelegate closure has source code but no compiled action.");
            object?[] captures = value.captures ?? Array.Empty<object?>();
            if (action.parameters is null
                || action.parameters.Length != args.Length + captures.Length + 2)
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate closure parameter envelope does not match {args.Length} call arguments and {captures.Length} captures.");
            }
            object? lexicalThis = value.hasLexicalEnvironment
                ? value.lexicalThis
                : ctx.thisValue;
            object? lexicalRoot = value.hasLexicalEnvironment
                ? value.lexicalRoot
                : ctx.rootValue;
            // A closure completes before its caller continues, so like an
            // immediate NSFunction it runs in a frame on the caller's context.
            // Only another root needs a fork: frames do not save the root.
            Context nestedCtx = ctx;
            int frame = -1;
            if (ReferenceEquals(lexicalRoot, ctx.rootValue))
            {
                frame = ctx.EnterThis(lexicalThis);
            }
            else
            {
                nestedCtx = ctx.WithThis(lexicalThis);
                nestedCtx.BindRoot(lexicalRoot);
            }
            NeoScriptScopeLayout layout = action.scopeLayout ??= new NeoScriptScopeLayout(action);
            var scope = layout.RentScope();
            bool completed = false;
            try
            {
                var options = NeoScriptExecutionOptions.ForImmediate(ctx.client);
                NeoScriptExecutor.PrepareFunctionContext(nestedCtx, options, frame);
                scope.SetParameter(0, lexicalThis);
                scope.SetParameter(1, lexicalRoot);
                for (int i = 0; i < args.Length; i++)
                {
                    scope.SetParameter(i + 2,
                        NeoScriptValueMarshaller.Normalize(
                            ctx.client,
                            ctx.valueOwnership,
                            args[i],
                            action.parameters[i + 2].typeInfo,
                            nestedCtx,
                            NeoScriptValueMarshaller.ValueSubject.Closure("argument", i)));
                }
                for (int i = 0; i < captures.Length; i++)
                {
                    int parameterIndex = i + args.Length + 2;
                    scope.SetParameter(parameterIndex,
                        NeoScriptValueMarshaller.Normalize(
                            ctx.client,
                            ctx.valueOwnership,
                            captures[i],
                            action.parameters[parameterIndex].typeInfo,
                            nestedCtx,
                            NeoScriptValueMarshaller.ValueSubject.Closure("capture", i)));
                }
                NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                    ctx.client,
                    action,
                    scope,
                    nestedCtx,
                    options);
                if (result.IsPaused)
                {
                    result.Deferred?.DisposeFromOwner("NeoDelegate closure suspended");
                    throw new NSGetterRuntimeError(
                        "NeoDelegate closure suspended; delegate calls require an immediate callable target.");
                }
                completed = true;
                return result.ReturnValue;
            }
            finally
            {
                if (frame >= 0)
                    ctx.ExitFunction(frame);
                if (completed)
                    layout.ReturnScope(scope);
                else
                    layout.AbandonScope(scope);
            }
        }

        /// <param name="ownerReceiver">
        /// The row that owns the NSAction being fanned out, when this call is
        /// one of its listeners. Only an action passes it: a delegate holds
        /// exactly one target and always spells its own receiver.
        /// </param>
        // The stack holds ids; names are only spelled out for an error.
        private static string DescribeDelegateCallStack(
            Context ctx,
            string memberId,
            string? valueId)
        {
            var text = new System.Text.StringBuilder();
            NeoDelegateFrameStack frames = ctx.delegateCallStack;
            for (int i = 0; i < frames.Count; i++)
                AppendDelegateFrame(text, ctx, frames.MemberId(i), frames.ValueId(i));
            AppendDelegateFrame(text, ctx, memberId, valueId);
            return text.ToString();
        }

        private static string DescribeDelegateFrame(
            Context ctx,
            string memberId,
            string? valueId)
        {
            var text = new System.Text.StringBuilder();
            AppendDelegateFrame(text, ctx, memberId, valueId);
            return text.ToString();
        }

        private static void AppendDelegateFrame(
            System.Text.StringBuilder text,
            Context ctx,
            string memberId,
            string? valueId)
        {
            if (text.Length != 0)
                text.Append(" -> ");
            string name = ctx.client.TryGetMember(memberId, out JsonMember? member)
                ? member.name
                : memberId;
            text.Append(name).Append('[').Append(valueId ?? "default").Append(']');
        }

        private static object? InvokeDelegateMemberTarget(
            NeoDelegateValue target,
            object?[] args,
            Context ctx,
            object? ownerReceiver = null,
            NeoValueOwnership? receiverOwnership = null)
        {
            string memberId = target.memberId!;
            NeoResolvedNSFunction? function = null;
            NeoClient.ResolvedNativeFunction? nativeFunction = null;
            JsonMember? member;
            // A resolved target holds its function's id instance, so the id
            // checks below match by reference without a string call.
            switch (target.resolvedTarget)
            {
                case NeoResolvedNSFunction cached
                    when ReferenceEquals(cached.SchemaResolution, ctx.client.SchemaResolution)
                        && (ReferenceEquals(cached.MemberId, memberId) || cached.MemberId == memberId):
                    function = cached;
                    member = cached.Member;
                    break;
                case NeoClient.ResolvedNativeFunction { member: { } cachedMember } cached
                    when ReferenceEquals(cached.schemaResolution, ctx.client.SchemaResolution)
                        && (ReferenceEquals(cached.memberId, memberId) || cached.memberId == memberId):
                    nativeFunction = cached;
                    member = cachedMember;
                    break;
                default:
                    if (!ctx.client.TryGetMember(memberId, out member))
                    {
                        throw new NSGetterRuntimeError(
                            $"NeoDelegate target member '{memberId}' does not exist.");
                    }
                    break;
            }
            // The cycle key is the bare target frame. A listener position is
            // reported in the fan-out's message, never folded into the key:
            // the same (member, row) re-entered at a different listener index
            // is the same frame, and the TS evaluator keys it that way too.
            NeoDelegateFrameStack frames = ctx.delegateCallStack;
            if (frames.Contains(memberId, target.valueId))
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target cycle: {DescribeDelegateCallStack(ctx, memberId, target.valueId)}.");
            }
            if (frames.Count >= NeoDelegateFrameStack.MaxDepth)
            {
                throw new NSGetterRuntimeError(
                    $"NeoDelegate call stack exceeded {NeoDelegateFrameStack.MaxDepth} frames: {DescribeDelegateCallStack(ctx, memberId, target.valueId)}.");
            }

            object? receiver = null;
            if (target.valueId is not null)
            {
                NeoValueNode? node = null;
                NeoValueOwnership ownership = receiverOwnership ?? ResolveOwnershipForValueId(
                    ctx,
                    target.valueId,
                    ref node);
                MemberValue row = ctx.client.ReadValue(ownership, target.valueId, ref node)
                    ?? throw new NSGetterRuntimeError(
                        $"NeoDelegate target '{member.name}' has missing receiver value '{target.valueId}'.");
                receiver = UnwrapCached(row, ctx, ownership, node: node);
            }
            else if (ownerReceiver is not null)
            {
                // P62 §3.3. A listener stored with a null `valueId` names no
                // row of its own, and both authored forms produce exactly
                // that: a declaration default cannot name a row, and
                // `this.OnX += this.Handler` lowers to it because the
                // resolver has no static row identity for `this`. Such a
                // listener runs against the row that owns the action —
                // otherwise the spec's `= [PlayHitFlash]` example would
                // execute `this.FlashFrames = 4` against a null `this`.
                //
                // Bound only when the listener member really is on that row's
                // class, so an action fanned out on one class can never
                // smuggle a foreign row in as `this`; anything else stays
                // receiver-less, the pre-P62 behavior.
                receiver = ListenerReceiverOnOwner(target, ownerReceiver, ctx);
            }

            frames.Push(memberId, target.valueId);
            try
            {
                if (member is FunctionMember native)
                {
                    if (nativeFunction is null && ctx.client.TryResolveNativeFunction(memberId, out nativeFunction))
                        target.resolvedTarget = nativeFunction;
                    if (nativeFunction?.signature.Dispatch == NeoFunctionDispatchKind.Asynchronous)
                    {
                        throw new NeoDeferredFunctionRuntimeError(
                            $"NeoDelegate target Function '{member.name}' is deferred; delegates require an immediate callable target.");
                    }
                    return InvokeNativeFunction(memberId, nativeFunction, native.returnTypeInfo, receiver, args, ctx);
                }
                if (member is NSFunctionMember)
                {
                    if (function is null)
                    {
                        function = NeoNSFunctionRuntime.ResolveSignature(ctx.client, memberId);
                        target.resolvedTarget = function;
                        // The client's cached function may carry another
                        // instance of the same id.
                        target.memberId = function.MemberId;
                    }
                    return NeoNSFunctionRuntime.InvokeImmediate(
                        ctx.client,
                        function,
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
                if (member is ActionMember actionMember)
                {
                    // An NSAction listener target is fan-out, not a single
                    // callable: invoking it invokes its own listeners
                    // (P62 §2.1), guarded by the same cycle detection and
                    // 64-frame cap this frame already pushed.
                    // The nested action's own owner is the receiver this
                    // member target resolved against, not the outer action's.
                    InvokeActionTarget(actionMember, receiver, args, ctx, memberId, target.valueId);
                    return null;
                }
                throw new NSGetterRuntimeError(
                    $"NeoDelegate target '{member.name}' resolves to non-callable member kind {member.kind}.");
            }
            finally
            {
                frames.Pop();
            }
        }

        // Its own method so the frame-describing lambda's closure is
        // allocated only on this branch, not on every member-target call.
        private static void InvokeActionTarget(
            ActionMember actionMember,
            object? receiver,
            object?[] args,
            Context ctx,
            string memberId,
            string? valueId)
        {
            InvokeAction(
                ResolveActionTargetValue(actionMember, receiver, ctx),
                args,
                ctx,
                receiver,
                () => DescribeDelegateFrame(ctx, memberId, valueId));
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
                if (resolved is NeoDelegateValue target)
                    return target;
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

        /// <summary>
        /// Evaluates an action call pointer and reports the row it read the
        /// action off (P62 §3.3), mirroring the TS
        /// <c>readActionWithOwner</c>.
        ///
        /// <para>Null-<c>valueId</c> listeners bind to that row (see
        /// <see cref="InvokeDelegateMemberTarget"/>) and it also names the
        /// invocation frame, so it must be identified without evaluating the
        /// receiver subexpression a second time: re-deriving it from the
        /// pointer shape would double every side effect on the way to the
        /// action and would drop the owner entirely for a deeper chain
        /// (<c>this.Child.OnEnter()</c>, <c>this.Rooms[0].OnEnter()</c>).
        /// The receiver is therefore reported out of the one evaluation that
        /// already happens. A pointer that is not a key access — a static
        /// action, a variable holding one — has no owning row to lend, and
        /// reports null.</para>
        /// </summary>
        private static object? ReadActionWithOwner(
            Pointer pointer,
            NeoScriptScope scope,
            Context ctx,
            out object? owner)
        {
            if (pointer is not KeyOfPointer keyOfPointer)
            {
                owner = null;
                return EvalPointer(pointer, scope, ctx);
            }
            owner = null;
            return EvalKeyOfReceiver(
                EvalPointer(keyOfPointer.keyOf.pointer, scope, ctx),
                keyOfPointer.keyOf,
                scope,
                ctx,
                keyOfPointer.optional == true,
                keyOfPointer.memberId,
                ref owner,
                reportReceiver: true);
        }

        /// <summary>
        /// The action member's display name for a call pointer, or the
        /// generic label when the pointer names no member (a variable or a
        /// computed expression). Mirrors the TS <c>actionPointerLabel</c>.
        /// </summary>
        private static string ActionPointerLabel(Pointer pointer, Context ctx)
        {
            string? memberId = pointer switch
            {
                StaticMemberPointer staticPointer => staticPointer.memberId,
                KeyOfPointer keyOfPointer => keyOfPointer.memberId,
                _ => null,
            };
            if (string.IsNullOrEmpty(memberId))
                return "action";
            return ctx.client.TryGetMember(memberId!, out JsonMember? member)
                ? member.name
                : memberId!;
        }

        private static void EvalCallAction(
            CallActionPointer actionCall,
            NeoScriptScope scope,
            Context ctx)
        {
            object? actionValue = ReadActionWithOwner(
                actionCall.action,
                scope,
                ctx,
                out object? owner);
            object?[] args = actionCall.args.Length == 0
                ? Array.Empty<object?>()
                : new object?[actionCall.args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = EvalPointer(actionCall.args[i], scope, ctx);
            }
            InvokeListeners(actionValue, args, ctx, owner, actionCall.action);
        }

        /// <summary>
        /// The <c>{actionMemberName}[{owningRowId ?? "default"}]</c> frame a
        /// failing listener is reported under — the <i>action</i> member and
        /// the row it fanned out from, never the listener's own identity, so
        /// both entry points and both runtimes name the same frame for the
        /// same failure (P62 §3.1).
        ///
        /// <para>Built lazily from the owner
        /// <see cref="ReadActionWithOwner"/> captured: naming the row is pure
        /// bookkeeping that the overwhelmingly common no-failure path should
        /// not pay for.</para>
        /// </summary>
        private static string ActionInvocationFrame(
            Pointer pointer,
            object? owner,
            Context ctx)
        {
            string name = ActionPointerLabel(pointer, ctx);
            if (owner is null)
                return $"{name}[default]";
            return FindRowReference(owner, ctx) is { } row
                ? $"{name}[{row.valueId}]"
                : $"{name}[default]";
        }

        /// <summary>
        /// The receiver a null-<c>valueId</c> listener binds to, or null when
        /// it must stay receiver-less.
        ///
        /// <paramref name="ownerReceiver"/> is the row the fanning-out action
        /// was read from. It is only a valid <c>this</c> for the listener when
        /// the listener member is reachable through that row's own runtime
        /// class surface — the same merged-schema lookup
        /// <see cref="DispatchSchemaMember"/> uses. A listener naming a member
        /// of some other class is a target the owner row cannot supply, so it
        /// runs with no receiver rather than with the wrong one.
        ///
        /// <para>The answer depends only on the owner's Class, so the
        /// listener keeps it per Class while the schema resolution
        /// holds.</para>
        /// </summary>
        private static object? ListenerReceiverOnOwner(
            NeoDelegateValue listener,
            object ownerReceiver,
            Context ctx)
        {
            if (AsObjectRecord(ownerReceiver) is null)
                return null;
            RowReference? ownerRef = FindRowReference(ownerReceiver, ctx);
            string? runtimeClassId = ownerRef is not null
                ? ClassIdOfRowReference(ownerRef, ctx)
                : FindRowClassIdByReference(ownerReceiver, ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
                return null;
            NeoClassNode classNode;
            try
            {
                classNode = ownerRef is not null
                    ? ownerRef.ClassNode(ctx.client, runtimeClassId!)
                    : ctx.client.ResolveClassNode(runtimeClassId!);
            }
            catch (CircularInheritanceError)
            {
                return null;
            }
            ListenerOwnerTarget? targets = listener.ownerTargets as ListenerOwnerTarget;
            if (targets is not null
                && !ReferenceEquals(targets.schemaResolution, ctx.client.SchemaResolution))
            {
                targets = null;
            }
            for (ListenerOwnerTarget? target = targets; target is not null; target = target.next)
            {
                if (ReferenceEquals(target.classNode, classNode))
                    return target.binds ? ownerReceiver : null;
            }
            bool binds = FindSchemaPlacementCached(listener.memberId!, ctx) is { } placement
                && classNode.SurfaceMember(placement.schemaKey) is { } entry
                && ctx.client.TryGetMember(entry.memberId, out JsonMember? _);
            if ((targets?.count ?? 0) < CallSiteTarget.MaxTargets)
            {
                listener.ownerTargets = new ListenerOwnerTarget(
                    ctx.client.SchemaResolution,
                    classNode,
                    binds,
                    targets);
            }
            return binds ? ownerReceiver : null;
        }

        /// <summary>
        /// Whether a null-<c>valueId</c> listener binds to an owner of one
        /// Class. Like <see cref="MemberSiteTarget"/>, a listener chains one
        /// target per owner Class and holds while the schema resolution does.
        /// </summary>
        private sealed class ListenerOwnerTarget
        {
            internal readonly object schemaResolution;
            internal readonly NeoClassNode classNode;
            internal readonly bool binds;
            internal readonly ListenerOwnerTarget? next;
            internal readonly int count;

            internal ListenerOwnerTarget(
                object schemaResolution,
                NeoClassNode classNode,
                bool binds,
                ListenerOwnerTarget? next)
            {
                this.schemaResolution = schemaResolution;
                this.classNode = classNode;
                this.binds = binds;
                this.next = next;
                count = (next?.count ?? 0) + 1;
            }
        }

        /// <summary>
        /// Reads the listener set an NSAction member target resolves to —
        /// the receiver's stored row when there is one, otherwise the nearest
        /// declaration default up the override chain. Unlike
        /// <see cref="ResolveDelegateTargetValue"/> an absent value is not an
        /// error: the empty set is an action's rest state (P62 §2.1).
        /// </summary>
        private static NeoActionValue ResolveActionTargetValue(
            ActionMember member,
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
                        $"NSAction target '{member.name}' is missing from its receiver.");
                }
                object? resolved = ResolveValueIfId(
                    raw,
                    ctx,
                    member: member);
                if (resolved is null)
                    return new NeoActionValue();
                if (resolved is NeoActionValue target)
                    return target;
                throw new NSGetterRuntimeError(
                    $"NSAction target '{member.name}' has an invalid stored value.");
            }

            if (member.Modifier == NeoMemberModifierKind.Static)
            {
                // A static action has no receiver to read through, but it
                // does have a live row: subscriptions write to it, so the
                // declaration default is not what a fan-out through it means.
                // Read it exactly as a staticMember pointer would, and mirror
                // the TS ordering — receiver, then static, then declaration
                // default.
                return StaticActionListeners(member, ctx);
            }

            var visited = new HashSet<string>();
            ActionMember? cursor = member;
            while (cursor is not null && visited.Add(cursor.id))
            {
                if (cursor.defaultValue?.value is NeoActionValue value)
                {
                    return value;
                }
                if (string.IsNullOrEmpty(cursor.extendsMemberId)
                    || !ctx.client.TryGetMember(
                        cursor.extendsMemberId,
                        out ActionMember? parent))
                {
                    break;
                }
                cursor = parent;
            }
            return new NeoActionValue();
        }

        /// <summary>
        /// The listener set bound to a static NSAction member. An unbound
        /// static member is the rest state — the empty set — the same answer
        /// the TS <c>evalStaticMember</c> gives a non-required member with no
        /// active binding.
        /// </summary>
        private static NeoActionValue StaticActionListeners(
            ActionMember member,
            Context ctx)
        {
            NeoValueOwnership ownership =
                ctx.client.ResolveStaticOwnership(member);
            if (!ctx.client.TryResolveStaticBinding(
                    member.id,
                    out _,
                    out _,
                    out string? staticValueId))
            {
                return new NeoActionValue();
            }
            if (!ctx.client.TryGetOverlaidValue(
                    ownership,
                    staticValueId,
                    out MemberValue? staticRow))
            {
                throw new NSGetterRuntimeError(
                    $"NSAction target '{member.name}' is bound to missing static value '{staticValueId}'.");
            }
            if (staticRow is not ActionMemberValue actionRow)
            {
                throw new NSGetterRuntimeError(
                    $"NSAction target '{member.name}' is bound to value '{staticValueId}', which is not a listener set.");
            }
            return actionRow.value ?? new NeoActionValue();
        }

        /// <summary>
        /// One resolved target of a call site. Resolution reads only the
        /// schema and, for interface dispatch, the receiver's runtime Class,
        /// so it holds while the client's schema resolution does. A site
        /// dispatched on several Classes chains one target per Class.
        /// </summary>
        internal sealed class CallSiteTarget
        {
            // Bounds a site that sees an unusual number of runtime Classes.
            internal const int MaxTargets = 16;

            internal readonly object schemaResolution;
            /// <summary>The runtime Class the target was dispatched on, or null when the receiver did not decide it.</summary>
            internal readonly string? receiverClassId;
            internal readonly string memberId;
            /// <summary>The NSFunction <see cref="memberId"/> names; null for a native Function.</summary>
            internal readonly NeoResolvedNSFunction? function;
            /// <summary>The native Function <see cref="memberId"/> names; null for an NSFunction or a missing member.</summary>
            internal readonly FunctionMember? native;
            /// <summary>The signature <see cref="native"/> calls fill and validate against: its own, or the one it extends.</summary>
            internal readonly NeoClient.ResolvedNativeFunction? nativeFunction;
            internal readonly CallSiteTarget? next;
            internal readonly int count;

            internal CallSiteTarget(
                NeoClient client,
                string? receiverClassId,
                string memberId,
                CallSiteTarget? next)
            {
                schemaResolution = client.SchemaResolution;
                this.receiverClassId = receiverClassId;
                this.memberId = memberId;
                if (client.TryGetMember(memberId, out NSFunctionMember? _))
                {
                    function = NeoNSFunctionRuntime.ResolveSignature(client, memberId);
                }
                else
                {
                    client.TryGetMember(memberId, out native);
                    client.TryResolveNativeFunction(memberId, out nativeFunction);
                }
                this.next = next;
                count = (next?.count ?? 0) + 1;
            }
        }

        /// <summary>
        /// <see cref="ResolveFunctionMemberId"/> through the call site's
        /// cached targets, or null when the call has no target. A repeat call
        /// on a runtime Class the site has seen costs class-id comparisons.
        /// </summary>
        /// <param name="receiverRow">The receiver's row, when the caller just read it.</param>
        internal static CallSiteTarget? ResolveCallTarget(
            CallFunctionPointer pointer,
            object? receiver,
            Context ctx,
            MemberValue? receiverRow = null)
        {
            // Targets are immutable and published with one reference write,
            // so a site shared across clients or threads reads a whole chain.
            CallSiteTarget? targets = pointer.resolvedTargets;
            if (targets is not null
                && !ReferenceEquals(targets.schemaResolution, ctx.client.SchemaResolution))
            {
                targets = null;
            }
            string? receiverClassId = null;
            bool receiverClassKnown = false;
            for (CallSiteTarget? target = targets; target is not null; target = target.next)
            {
                if (target.receiverClassId is not null)
                {
                    if (!receiverClassKnown)
                    {
                        receiverClassId = FindRowClassIdByReference(receiver, ctx, receiverRow);
                        receiverClassKnown = true;
                    }
                    if (!SameId(target.receiverClassId, receiverClassId))
                        continue;
                }
                return target;
            }
            string? memberId = ResolveFunctionMemberId(
                pointer,
                receiver,
                ctx,
                out string? dispatchClassId,
                out bool cacheable);
            if (memberId is null)
                return null;
            ValidateValueEqualitySignature(pointer, memberId, ctx);
            if (!cacheable || (targets?.count ?? 0) >= CallSiteTarget.MaxTargets)
            {
                // Not a dispatch the site can answer from its chain, but its
                // last answer still serves a repeat of the same member.
                CallSiteTarget? last = pointer.uncachedTarget;
                bool remembered = last is not null
                    && ReferenceEquals(last.schemaResolution, ctx.client.SchemaResolution);
                if (remembered && SameId(last!.memberId, memberId))
                    return last;
                var created = new CallSiteTarget(ctx.client, dispatchClassId, memberId, null);
                pointer.uncachedTarget = created;
                if (!remembered)
                    ctx.client.RememberSchemaResolutionSite(pointer);
                return created;
            }
            var resolved = new CallSiteTarget(ctx.client, dispatchClassId, memberId, targets);
            pointer.resolvedTargets = resolved;
            if (targets is null)
                ctx.client.RememberSchemaResolutionSite(pointer);
            return resolved;
        }

        /// <param name="dispatchClassId">The receiver's runtime Class when it decided the target.</param>
        /// <param name="cacheable">False when the answer depends on more of the receiver than its Class.</param>
        private static string? ResolveFunctionMemberId(
            CallFunctionPointer pointer,
            object? receiver,
            Context ctx,
            out string? dispatchClassId,
            out bool cacheable)
        {
            dispatchClassId = null;
            cacheable = true;
            if (pointer.dispatch == "base")
            {
                if (pointer.receiver.IsStatic)
                    throw new NSGetterRuntimeError("Base dispatch requires an instance receiver.");
                if (string.IsNullOrEmpty(pointer.memberId))
                    throw new NSGetterRuntimeError("Base dispatch requires an explicit memberId.");
                if (pointer.memberKey is not null)
                    throw new NSGetterRuntimeError("Base dispatch cannot contain memberKey.");
                return pointer.memberId;
            }
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
                if (placement is null)
                    return pointer.memberId!;
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
                cacheable = false;
                if (!string.IsNullOrEmpty(pointer.memberId))
                {
                    return pointer.memberId!;
                }
                if (pointer.missingMemberFallback == "valueEquality")
                    return null;
                throw new NSGetterRuntimeError(
                    $"Cannot resolve interface Function member '{schemaKey}' because the receiver has no runtime class.");
            }

            dispatchClassId = runtimeClassId;
            (string, string) dispatchCacheKey = (runtimeClassId!, schemaKey!);
            if (ctx.callableDispatchCache.TryGetValue(
                    dispatchCacheKey, out string? cachedMemberId))
            {
                if (cachedMemberId is null)
                {
                    if (pointer.missingMemberFallback == "valueEquality")
                        return null;
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
                if (entry.schemaKey != schemaKey)
                    continue;
                if (!TryResolveCallableKind(ctx.client, entry.memberId))
                {
                    throw new NSGetterRuntimeError(
                        $"Runtime class '{runtimeClassId}' member '{schemaKey}' is not a Function member.");
                }
                ctx.callableDispatchCache[dispatchCacheKey] = entry.memberId;
                return entry.memberId;
            }
            ctx.callableDispatchCache[dispatchCacheKey] = null;
            if (pointer.missingMemberFallback == "valueEquality")
                return null;
            throw new NSGetterRuntimeError(
                $"Runtime class '{runtimeClassId}' does not implement Function member '{schemaKey}'.");
        }

        internal static object? EvaluateMissingMemberFallback(
            CallFunctionPointer pointer,
            object? receiver,
            object?[] args)
        {
            if (pointer.missingMemberFallback != "valueEquality" || args.Length != 1)
            {
                throw new NSGetterRuntimeError(
                    "Function call has no runtime member and no valid missing-member fallback.");
            }
            return Box(JsEqual(receiver, args[0]));
        }

        internal static void ValidateValueEqualitySignature(
            CallFunctionPointer pointer,
            string memberId,
            Context ctx)
        {
            if (pointer.missingMemberFallback != "valueEquality")
                return;
            TypeInfo? returnTypeInfo;
            int argumentCount;
            if (ctx.client.TryGetMember(memberId, out NSFunctionMember? nsFunction))
            {
                NeoResolvedNSFunction resolved = NeoNSFunctionRuntime.ResolveSignature(
                    ctx.client,
                    nsFunction!.id);
                returnTypeInfo = resolved.ReturnTypeInfo;
                argumentCount = resolved.ArgumentTypes.Length;
            }
            else if (ctx.client.TryResolveFunctionMember(
                         memberId,
                         out FunctionMember? function))
            {
                returnTypeInfo = function!.returnTypeInfo;
                argumentCount = function.argumentTypes.Length;
            }
            else
            {
                throw new NSGetterRuntimeError(
                    $"Generic Equals member '{memberId}' has no resolvable signature.");
            }
            if (argumentCount != 1)
            {
                throw new NSGetterRuntimeError(
                    $"Generic Equals member '{memberId}' must take exactly one argument; found {argumentCount}.");
            }
            if (returnTypeInfo.type != MemberKind.Bool)
            {
                throw new NSGetterRuntimeError(
                    $"Generic Equals member '{memberId}' must return bool.");
            }
        }

        internal static object? EvalCallReceiver(
            CallReceiver receiver,
            NeoScriptScope scope,
            Context ctx)
        {
            if (receiver is null)
            {
                throw new NSGetterRuntimeError("Callable pointer is missing its receiver.");
            }
            if (receiver.IsStatic)
                return null;
            if (!receiver.IsInstance || receiver.pointer is null)
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
            ctx.client.TryGetMember(targetMemberId, out JsonMember? member);
            ValidateStaticCallableReceiver(receiver, targetMemberId, callableKind, member);
        }

        /// <param name="member">The member <paramref name="targetMemberId"/> names, if any.</param>
        private static void ValidateStaticCallableReceiver(
            CallReceiver receiver,
            string targetMemberId,
            string callableKind,
            JsonMember? member)
        {
            if (string.IsNullOrEmpty(receiver.memberId))
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} call receiver is missing its member id.");
            }
            if (!ReferenceEquals(receiver.memberId, targetMemberId) && receiver.memberId != targetMemberId)
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} call receiver '{receiver.memberId}' does not match target '{targetMemberId}'.");
            }
            if (member is null || member.Modifier != NeoMemberModifierKind.Static)
            {
                throw new NSGetterRuntimeError(
                    $"Static {callableKind} target '{targetMemberId}' is missing or is not static.");
            }
        }

        private static bool EvalFunctionErrorCheck(
            FunctionErrorCheckPointer pointer,
            NeoScriptScope scope,
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
                if (!client.TryGetMember(cursor!, out JsonMember? member))
                    return false;
                expectedType ??= member.kind;
                if (member.kind != expectedType
                    || (member.kind != MemberKind.Function
                        && member.kind != MemberKind.NSFunction))
                {
                    return false;
                }
                if (member is FunctionMember or NSFunctionMember)
                    return true;
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
            SchemaPlacement? placement = ctx.client.FindSchemaPlacement(memberId);
            ctx.schemaPlacementCache[memberId] = placement;
            return placement;
        }

        /// <summary>
        /// A getter call site's schema placement. It reads only the schema, so
        /// it holds while the client's schema resolution does.
        /// </summary>
        internal sealed class PlacementSite
        {
            internal readonly object schemaResolution;
            internal readonly SchemaPlacement? placement;

            internal PlacementSite(object schemaResolution, SchemaPlacement? placement)
            {
                this.schemaResolution = schemaResolution;
                this.placement = placement;
            }
        }

        private static SchemaPlacement? FindSchemaPlacementCached(
            CallGetterPointer site,
            Context ctx)
        {
            // Immutable and published with one reference write, as call-site
            // targets are.
            PlacementSite? cached = site.placementSite;
            if (cached is not null
                && ReferenceEquals(cached.schemaResolution, ctx.client.SchemaResolution))
            {
                return cached.placement;
            }
            SchemaPlacement? placement = FindSchemaPlacementCached(site.memberId, ctx);
            site.placementSite = new PlacementSite(ctx.client.SchemaResolution, placement);
            ctx.client.RememberSchemaResolutionSite(site);
            return placement;
        }

        // ---------------------------------------------------------------
        // KeyOf — schema-key dispatch with runtime-classId override hook
        // ---------------------------------------------------------------

        private static object? EvalKeyOf(
            KeyOf keyOf,
            NeoScriptScope scope,
            Context ctx,
            bool optional,
            string? pinnedMemberId)
        {
            object? receiver;
            if (ctx.client.PendingScriptWrites is { HasCollections: true } batch
                && keyOf.pendingUnreachedShape != batch.Shape
                && MayReachPending(keyOf, batch, scope, ctx))
            {
                if (EvalPendingCollection(keyOf.pointer, PendingRead.Entry, scope, ctx, out receiver) is { } pending)
                    return ReadPendingEntry(pending, keyOf.key, scope, ctx);
            }
            else
                receiver = EvalPointer(keyOf.pointer, scope, ctx);
            object? unused = null;
            return EvalKeyOfReceiver(
                receiver,
                keyOf,
                scope,
                ctx,
                optional,
                pinnedMemberId,
                ref unused,
                reportReceiver: false);
        }

        /// <summary>
        /// Reads <paramref name="pointer"/> off a receiver the caller already
        /// evaluated, so a write that inspected the receiver first does not
        /// evaluate it a second time.
        /// </summary>
        internal static object? EvaluateKeyOf(
            KeyOfPointer pointer,
            object? receiver,
            NeoScriptScope scope,
            Context ctx)
        {
            object? unused = null;
            return EvalKeyOfReceiver(
                receiver,
                pointer.keyOf,
                scope,
                ctx,
                pointer.optional == true,
                pointer.memberId,
                ref unused,
                reportReceiver: false);
        }

        /// <param name="reportedReceiver">
        /// Receives the evaluated receiver when
        /// <paramref name="reportReceiver"/> is set (P62 §3.3). An action
        /// call needs the row it read the action off, and this is the only
        /// way to get it without evaluating the receiver subexpression a
        /// second time. Reported after unwrapping, so the caller sees the
        /// same row the key access dispatched against.
        /// </param>
        private static object? EvalKeyOfReceiver(
            object? receiver,
            KeyOf keyOf,
            NeoScriptScope scope,
            Context ctx,
            bool optional,
            string? pinnedMemberId,
            ref object? reportedReceiver,
            bool reportReceiver)
        {
            if (optional && receiver is null)
                return null;
            // A list unwraps to itself.
            if (receiver is object?[] list)
            {
                if (reportReceiver)
                    reportedReceiver = receiver;
                return ReadListEntry(list, keyOf.key, scope, ctx);
            }
            // A record this context unwrapped reads its row here rather than
            // in UnwrapGeneratedValue, so dispatch on a constant key takes
            // the receiver's Class from that row instead of reading it again.
            MemberValue? receiverRow = null;
            if (receiver is NeoObjectRecord { reference: { } unwrappedRef } unwrappedRecord
                && ReferenceEquals(unwrappedRecord.referenceIndex, ctx.rowReverseIndex)
                && SameId(unwrappedRef.valueId, unwrappedRecord.valueId)
                && unwrappedRef.ownership == unwrappedRecord.valueOwnership)
            {
                receiverRow = ctx.client.ReadReplayReference(unwrappedRef.valueId, ref unwrappedRef.node, unwrappedRef.ownership);
                if (receiverRow is not null)
                {
                    receiver = UnwrapCached(receiverRow, ctx, unwrappedRef.ownership, unwrappedRef.member, unwrappedRef.node);
                    if (!ReferenceEquals(receiver, unwrappedRecord) || keyOf.key is not ValuePointer)
                        receiverRow = null;
                }
            }
            else
            {
                receiver = UnwrapGeneratedValue(receiver, ctx);
            }
            if (reportReceiver)
                reportedReceiver = receiver;
            if (receiver is object?[] arr)
                return ReadListEntry(arr, keyOf.key, scope, ctx);
            var key = EvalPointer(keyOf.key, scope, ctx);
            if (receiver is null)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot read property '{key}' of null");
            }

            string k = key as string ?? key?.ToString() ?? "null";
            if (receiver is NeoScriptObject detached)
            {
                if (TryReadDetachedMember(detached, k, ctx, out object? detachedValue, keyOf))
                    return detachedValue;
                receiver = ForwardDetached(detached, ctx);
            }
            // Components and channels are the only single-character keys.
            if (k.Length == 1)
            {
                if (TryReadVectorComponent(receiver, k, out float component))
                {
                    return Box(component);
                }
                // P42 §3. Colour channels read exactly like vector components.
                // Before P42 a `ColorMemberValue` unwrapped to a bare
                // `NeoColorValue`, which is neither a vector nor an
                // `IDictionary`, so `Tint.a` fell through to the "cannot index
                // into" throw below while the TS evaluator read it happily.
                if (TryReadColorComponent(receiver, k, out float channel))
                {
                    return Box(channel);
                }
            }
            // Tested by length first: a member key rarely has two characters.
            if (k.Length == 2 && k == "Id")
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
            if (AsObjectRecord(receiver) is { } record)
            {
                // Schema-dispatch if the receiver is a tracked Class row.
                object? dispatched = DispatchSchemaMember(
                    receiver,
                    k,
                    ctx,
                    keyOf,
                    receiverRow);
                if (Dispatched(dispatched))
                    return dispatched;
                // Interface/static-type pointers retain the compile-time
                // declaration id. Use it only when the concrete runtime Class
                // had no member at this key; a concrete stored override must
                // remain authoritative over a read-only base declaration.
                if (!ReferenceEquals(dispatched, DispatchMatchedNoValue)
                    && !string.IsNullOrEmpty(pinnedMemberId)
                    && ctx.client.TryGetMember(pinnedMemberId!, out JsonMember? pinnedMember)
                    && pinnedMember.Mutability == NeoMemberMutabilityKind.ReadOnly)
                {
                    return ReadOnlyDeclarationDefault(pinnedMember, ctx);
                }
                if (record!.TryGetValue(k, out var at))
                {
                    // A primitive is its own value: only a value id
                    // resolves through the record's row.
                    if (at is not string)
                        return at;
                    RowReference? recordRef = FindRowReference(receiver, ctx);
                    return ResolveValueIfId(
                        at,
                        ctx,
                        RowOwnership(recordRef, receiver),
                        CollectionEntryMember(recordRef, receiver, ctx));
                }
                throw new NSGetterRuntimeError($"Missing key '{k}' on object");
            }

            throw new NSGetterRuntimeError(
                $"Cannot index into {ReceiverTypeName(receiver)} with key '{key}'");
        }

        // List indexing: numeric keys are positional; String keys are
        // exact stable value ids (including numeric-looking strings).
        private static object? ReadListEntry(object?[] list, Pointer keyPointer, NeoScriptScope scope, Context ctx)
        {
            int idx;
            // A number variable indexes without boxing its value.
            if (keyPointer is VariablePointer indexVariable && scope.TryReadNumber(indexVariable, out double position))
            {
                idx = ListPosition(list.Length, position);
            }
            else
            {
                var key = EvalPointer(keyPointer, scope, ctx);
                if (key is string valueId)
                {
                    Dictionary<string, int> identity = ListIdentityIndexes.GetValue(
                        list,
                        entries => BuildListIdentityIndex((object?[])entries));
                    if (!identity.TryGetValue(valueId, out idx))
                    {
                        throw new NSGetterRuntimeError(
                            $"Value id '{valueId}' is not a member of this List");
                    }
                }
                else if (TryAsDouble(key, out position))
                {
                    idx = ListPosition(list.Length, position);
                }
                else
                {
                    throw new NSGetterRuntimeError($"List index must be an integer; got '{key}'");
                }
            }
            // A primitive entry is its own value: only a value id
            // resolves through the list's row.
            object? entry = list[idx];
            if (entry is not string)
                return entry;
            RowReference? listRef = FindRowReference(list, ctx);
            return ResolveValueIfId(entry, ctx, listRef?.ownership, CollectionEntryMember(listRef, list, ctx));
        }

        /// <summary>What a read of a pending collection asks, which decides the collections it can answer.</summary>
        internal enum PendingRead
        {
            /// <summary>The entry count: a List, Dictionary or multi-select Lookup.</summary>
            Count,
            /// <summary>One entry by position, id or key: a List or Dictionary.</summary>
            Entry,
            /// <summary>Whether an entry has a given id: a List or multi-select Lookup.</summary>
            Membership,
            /// <summary>The collection a mutation changes: any pending collection.</summary>
            Target,
        }

        /// <summary>
        /// The held write batch's pending collection <paramref name="pointer"/>
        /// names, found without reading it, when it can answer
        /// <paramref name="read"/> (see <see cref="Answers"/>); otherwise
        /// null, with <paramref name="value"/> the pointer's value. A read commits a
        /// pending collection, whose row costs its length to store, so N
        /// reads among N mutations would cost N².
        /// </summary>
        internal static NeoWriteBatch.PendingCollection? EvalPendingCollection(
            Pointer pointer,
            PendingRead read,
            NeoScriptScope scope,
            Context ctx,
            out object? value) =>
            FindPending(pointer, read, scope, ctx, evaluate: true, out value, out _, out _);

        /// <summary>
        /// The pending collection a mutation of <paramref name="pointer"/>
        /// targets, found without reading it. Otherwise null, with
        /// <paramref name="receiver"/> the key's receiver, and, when the
        /// target is an entry of a pending collection, <paramref name="owner"/>
        /// that collection and <paramref name="entry"/> the entry.
        /// </summary>
        internal static NeoWriteBatch.PendingCollection? FindPendingTarget(
            Pointer pointer,
            NeoScriptScope scope,
            Context ctx,
            out object? receiver,
            out NeoWriteBatch.PendingCollection? owner,
            out object? entry) =>
            FindPending(pointer, PendingRead.Target, scope, ctx, evaluate: false, out entry, out receiver, out owner);

        /// <summary>
        /// Whether <paramref name="pointer"/> may name a pending collection
        /// or an entry of one, decided from its shape: a read that can't
        /// takes the ordinary path, which costs what it costs with no batch.
        /// </summary>
        private static bool MayReachPending(KeyOf site, NeoWriteBatch batch, NeoScriptScope scope, Context ctx)
        {
            // Constant keys off a root reference reach the same rows each
            // time, so the site remembers the batch shape that proved it can't.
            bool constant = true;
            KeyOf keyOf = site;
            while (true)
            {
                // An index reaches an entry, pending only as its list is,
                // unless an entry is itself a pending collection.
                Pointer key = keyOf.key;
                if (key is ValuePointer { primitiveIsNumber: true })
                {
                    if (batch.HasUnkeyedMembers)
                        return true;
                }
                else if (key is ValuePointer)
                {
                    if (batch.HasUnkeyedMembers || EvalPointer(key, scope, ctx) is not string field || batch.IsFieldKey(field))
                        return true;
                }
                else if (key is VariablePointer variable && scope.TryReadNumber(variable, out _))
                {
                    if (batch.HasUnkeyedMembers)
                        return true;
                    constant = false;
                }
                else
                    return true;
                switch (keyOf.pointer)
                {
                    case KeyOfPointer receiver:
                        keyOf = receiver.keyOf;
                        continue;
                    case ReferencePointer reference:
                        if (batch.HasCollection(reference.valueId))
                            return true;
                        // So does every receiver along it.
                        for (KeyOf? proven = constant ? site : null; proven is not null; proven = (proven.pointer as KeyOfPointer)?.keyOf)
                            proven.pendingUnreachedShape = batch.Shape;
                        return false;
                    case VariablePointer:
                    case StaticMemberPointer:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>The pending collection at root reference <paramref name="valueId"/>.</summary>
        internal static NeoWriteBatch.PendingCollection? PendingReference(NeoWriteBatch batch, string valueId, Context ctx) =>
            batch.HasCollection(valueId) ? batch.Collection(ResolveOwnershipForValueId(ctx, valueId), valueId) : null;

        /// <param name="evaluate">Whether a miss evaluates the pointer's value; an entry of a pending collection always does.</param>
        private static NeoWriteBatch.PendingCollection? FindPending(
            Pointer pointer,
            PendingRead read,
            NeoScriptScope scope,
            Context ctx,
            bool evaluate,
            out object? value,
            out object? receiver,
            out NeoWriteBatch.PendingCollection? owner)
        {
            NeoWriteBatch batch = ctx.client.PendingScriptWrites!;
            receiver = null;
            owner = null;
            if (pointer is KeyOfPointer keyOf)
            {
                // The receiver resolves the same way, so an entry of a pending
                // collection reads through its pending row.
                if (FindPending(keyOf.keyOf.pointer, PendingRead.Entry, scope, ctx, evaluate: true, out receiver, out _, out _) is { } outer)
                {
                    owner = outer;
                    string? entryId = PendingEntryId(outer, keyOf.keyOf.key, scope, ctx);
                    if (entryId is not null && Answers(batch, outer.Ownership, entryId, read) is { } nested)
                    {
                        value = null;
                        return nested;
                    }
                    value = entryId is null ? outer.Row.id : ResolvePendingEntry(outer, entryId, ctx);
                    return null;
                }
                if (keyOf.keyOf.key is ValuePointer
                    && EvalPointer(keyOf.keyOf.key, scope, ctx) is string key
                    && StoredMemberId(batch, receiver, key, ctx, out NeoValueOwnership memberOwnership) is string memberId
                    && Answers(batch, memberOwnership, memberId, read) is { } member)
                {
                    value = null;
                    return member;
                }
                value = evaluate ? EvaluateKeyOf(keyOf, receiver, scope, ctx) : null;
                return null;
            }
            if (pointer is ReferencePointer reference
                && PendingReference(batch, reference.valueId, ctx) is { } referenced
                && Answers(batch, referenced, read))
            {
                value = null;
                return referenced;
            }
            if (pointer is StaticMemberPointer staticMember
                && ctx.client.TryResolveStaticBinding(staticMember.memberId, out Member? bindingMember, out NeoValueOwnership staticOwnership, out string? staticValueId)
                && batch.Collection(staticOwnership, staticValueId) is { } bound
                && Answers(batch, bound, read, bindingMember))
            {
                value = null;
                return bound;
            }
            if (pointer is VariablePointer variable)
            {
                RowReference? alias = null;
                object? raw = scope.ReadVariable(variable, ctx.rowReverseIndex, out bool found, out bool remembered, ref alias);
                // A Class record is never a collection.
                RowReference? aliasReference = !found ? null
                    : raw is object?[] entries ? remembered ? alias : ctx.CollectionRowReference(entries)
                    : raw is NeoObjectRecord { reference: { classId: not null } } ? null
                    : FindRowReference(raw, ctx);
                if (aliasReference is not null && Answers(batch, aliasReference.ownership, aliasReference.valueId, read) is { } aliased)
                {
                    value = null;
                    return aliased;
                }
                // Only a list alias or a detached object reads anything more.
                if (found && raw is not object?[] && raw is not NeoScriptObject)
                {
                    value = raw;
                    return null;
                }
            }
            value = evaluate ? EvalPointer(pointer, scope, ctx) : null;
            return null;
        }

        /// <summary>
        /// The row id <paramref name="receiver"/>'s row stores at
        /// <paramref name="key"/>, found without reading that row; null when
        /// no pending collection can be a member of <paramref name="receiver"/>.
        /// </summary>
        private static string? StoredMemberId(
            NeoWriteBatch batch, object? receiver, string key, Context ctx, out NeoValueOwnership ownership)
        {
            // A member lives in its receiver's store.
            RowReference? reference = FindRowReference(receiver, ctx);
            string? receiverId = reference?.valueId ?? (receiver as INeoValueReference)?.valueId;
            if (string.IsNullOrEmpty(receiverId))
            {
                ownership = default;
                return null;
            }
            ownership = reference?.ownership ?? ResolveOwnershipForValueId(ctx, receiverId!);
            if (batch.PendingField(ownership, receiverId!, key) is { } pending)
                return pending;
            return batch.MayParent(ownership, receiverId!)
                && ctx.client.TryGetOverlaidValue(ownership, receiverId!, out MemberValue? receiverRow)
                && receiverRow is ObjectMemberValue { value: { } fields }
                && fields.TryGetValue(key, out string? memberId)
                    ? memberId
                    : null;
        }

        /// <summary>The pending collection at <paramref name="id"/> when it answers <paramref name="read"/>.</summary>
        private static NeoWriteBatch.PendingCollection? Answers(NeoWriteBatch batch, NeoValueOwnership ownership, string id, PendingRead read) =>
            batch.Collection(ownership, id) is { } pending && Answers(batch, pending, read) ? pending : null;

        /// <param name="member">The collection's member, when the read knows it: a static's row has no parent to infer it from.</param>
        private static bool Answers(
            NeoWriteBatch batch, NeoWriteBatch.PendingCollection pending, PendingRead read, Member? member = null)
        {
            if (read == PendingRead.Target)
                return true;
            batch.ResolveMembers(pending, member);
            return pending.CollectionMember switch
            {
                ListMember => read != PendingRead.Membership || pending.EntryMember is ClassMember,
                DictionaryMember => read != PendingRead.Membership,
                LookupMember { Selection: NeoMemberSelectionKind.Multi } => read != PendingRead.Entry,
                _ => false,
            };
        }

        /// <summary>A pending List or Dictionary's entry, as <see cref="ReadListEntry"/> and a key read find it.</summary>
        private static object? ReadPendingEntry(
            NeoWriteBatch.PendingCollection pending,
            Pointer keyPointer,
            NeoScriptScope scope,
            Context ctx)
        {
            string? entryId = PendingEntryId(pending, keyPointer, scope, ctx);
            return entryId is null ? pending.Row.id : ResolvePendingEntry(pending, entryId, ctx);
        }

        /// <summary>The id of a pending List or Dictionary's entry; null for a Dictionary's <c>Id</c> key, which reads the row's own id.</summary>
        private static string? PendingEntryId(
            NeoWriteBatch.PendingCollection pending,
            Pointer keyPointer,
            NeoScriptScope scope,
            Context ctx)
        {
            if (pending is NeoWriteBatch.PendingList list)
                return PendingListEntry(list, keyPointer, scope, ctx);
            var key = EvalPointer(keyPointer, scope, ctx);
            string k = key as string ?? key?.ToString() ?? "null";
            if (k == "Id")
                return null;
            if (!((ObjectMemberValue)pending.Row).value!.TryGetValue(k, out string? entryId))
                throw new NSGetterRuntimeError($"Missing key '{k}' on object");
            return entryId;
        }

        private static object? ResolvePendingEntry(NeoWriteBatch.PendingCollection pending, string entryId, Context ctx)
        {
            ctx.client.NotePendingRead(pending.Row.id);
            return ResolveValueIfId(entryId, ctx, pending.Ownership, pending.EntryMember);
        }

        private static string PendingListEntry(
            NeoWriteBatch.PendingList list,
            Pointer keyPointer,
            NeoScriptScope scope,
            Context ctx)
        {
            if (keyPointer is VariablePointer indexVariable && scope.TryReadNumber(indexVariable, out double position))
                return list[ListPosition(list.Count, position)];
            var key = EvalPointer(keyPointer, scope, ctx);
            if (key is string valueId)
            {
                if (!list.Contains(valueId))
                    throw new NSGetterRuntimeError($"Value id '{valueId}' is not a member of this List");
                return valueId;
            }
            if (TryAsDouble(key, out position))
                return list[ListPosition(list.Count, position)];
            throw new NSGetterRuntimeError($"List index must be an integer; got '{key}'");
        }

        private static int ListPosition(int length, double position)
        {
            if (!NeoNumbers.IsWhole(position))
                throw new NSGetterRuntimeError($"List index must be an integer; got '{position}'");
            if (position < 0 || position >= length)
            {
                throw new NSGetterRuntimeError(
                    $"List index out of bounds: {position}");
            }
            return (int)position;
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

        // Ordinal equality for ids, which are usually one shared instance:
        // the reference test answers those without a call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SameId(string? a, string? b) => ReferenceEquals(a, b) || string.Equals(a, b);

        // DispatchSchemaMember's answers when it read nothing. A plain return
        // rather than a result struct: Mono copies a returned struct that
        // holds a reference through a write-barriered range copy.
        private static readonly object DispatchNoMember = new();
        // The runtime Class declares the member but gave no value.
        private static readonly object DispatchMatchedNoValue = new();

        private static bool Dispatched(object? result) =>
            !ReferenceEquals(result, DispatchNoMember) && !ReferenceEquals(result, DispatchMatchedNoValue);

        /// <summary>A dispatch's value, or null when it read nothing.</summary>
        private static object? DispatchedValue(object? result) => Dispatched(result) ? result : null;

        /// <summary>
        /// Runtime member-access dispatch on a Class record. Mirrors
        /// the TS-side <c>dispatchSchemaMember</c>. Recovers the
        /// receiver's runtime <c>classId</c> by reference-equality
        /// against tracked rows, walks the merged schema for that
        /// type, and dispatches to either an NSProperty (if the merged
        /// entry is one and has a compiled getter) or a stored-field
        /// read.
        /// </summary>
        /// <param name="receiverRow">The receiver's row, when the caller just read it.</param>
        private static object? DispatchSchemaMember(
            object? receiver,
            string schemaKey,
            Context ctx,
            ISchemaResolutionSite? site = null,
            MemberValue? receiverRow = null)
        {
            if (receiver is NeoScriptObject { attachedId: null } detached)
            {
                return TryReadDetachedMember(detached, schemaKey, ctx, out object? detachedValue)
                    ? detachedValue
                    : DispatchSchemaMember(ForwardDetached(detached, ctx), schemaKey, ctx, site);
            }
            if (AsObjectRecord(receiver) is not { } record)
            {
                return DispatchNoMember;
            }

            // Recover the row by reference equality on `.value`. One reverse
            // lookup serves every provenance question this dispatch asks.
            RowReference? receiverRef = FindRowReference(receiver, ctx);
            // FindRowClassIdByReference without repeating the lookup that
            // just missed; a detached receiver returned above.
            string? runtimeClassId = receiverRef is not null
                ? ClassIdOfRowReference(receiverRef, ctx, receiverRow)
                : FindReferencedClassId(receiver, ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
            {
                return DispatchNoMember;
            }
            string? receiverRowId = receiverRef?.valueId;
            NeoValueOwnership? receiverOwnership = receiverRef is not null
                ? receiverRef.ownership
                : receiver is NeoObjectRecord receiverRecord ? receiverRecord.valueOwnership : null;

            MergedSchemaEntry? entry = SurfaceRowMember(receiverRef, runtimeClassId!, schemaKey, site, ctx);
            JsonMember? member = entry?.member;
            if (member is null)
            {
                return DispatchNoMember;
            }

            if (member is GenericMember)
            {
                member = NeoGenericResolution.SubstituteMember(
                    ctx.client,
                    member,
                    NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, receiver, ctx, $"Member '{member.name}'"));
            }

            if (member.Mutability == NeoMemberMutabilityKind.ReadOnly)
            {
                return ReadOnlyDeclarationDefault(member, ctx);
            }

            if (member.kind == MemberKind.NSProperty)
            {
                if (entry!.member is not NSPropertyMember { getter: { } getter })
                {
                    return DispatchMatchedNoValue;
                }
                return DispatchNSGetterById(entry.memberId, receiver, ctx, getter, receiverRef);
            }

            ctx.client.ReadReplayField(receiverRowId, schemaKey);
            if (receiverRowId is not null)
                ctx.client.NoteRowRead(receiverRowId, ref receiverRef!.capturedIn);
            var storedRecord = record as NeoObjectRecord;
            int storedSlot = storedRecord?.StoredSlot(entry!) ?? -1;
            object? at = null;
            if (storedSlot >= 0 || record!.TryGetValue(schemaKey, out at))
            {
                NeoValueNode? childNode;
                if (storedSlot >= 0)
                {
                    at = storedRecord!.StoredId(storedSlot);
                    childNode = storedRecord.StoredNode(storedSlot);
                }
                else
                {
                    childNode = storedRecord?.ChildNode(entry!, at);
                }
                object? child = ResolveValueIfId(at, ctx, receiverOwnership, member, ref childNode);
                if (storedSlot >= 0)
                    storedRecord!.RememberStoredNode(storedSlot, childNode);
                else
                    storedRecord?.RememberChildNode(entry!, at, childNode);
                return child;
            }
            if (receiverRef?.member is ClassMember { Payload: NeoMemberPayloadKind.Partial })
            {
                return DispatchMatchedNoValue;
            }
            // P75: a collapse-stamped row stores only the members that differ
            // from its construction — an absent key is usually a VIRTUAL
            // child indexed at its deterministic id, not an authored
            // omission. Resolve it before concluding anything from the
            // absence, exactly as the web evaluator does.
            if (!string.IsNullOrEmpty(receiverRowId))
            {
                // The record remembers its own row's children only.
                var ownRecord = storedRecord is not null
                    && SameId(storedRecord.valueId, receiverRowId)
                        ? storedRecord
                        : null;
                int virtualEpoch = ctx.client.VirtualClassChildrenEpoch;
                string? virtualChildId = ownRecord?.VirtualChildId(entry!, virtualEpoch);
                if (virtualChildId is not null
                    || ctx.client.TryGetVirtualClassChildValueId(
                        receiverRowId!,
                        schemaKey,
                        out virtualChildId)
                    && !string.IsNullOrEmpty(virtualChildId))
                {
                    NeoValueNode? childNode = ownRecord?.ChildNode(entry!, virtualChildId);
                    object? child = ResolveValueIfId(virtualChildId, ctx, receiverOwnership, member, ref childNode);
                    if (virtualEpoch >= 0)
                        ownRecord?.RememberChildNode(entry!, virtualChildId, childNode, virtualEpoch);
                    return child;
                }
            }
            // A row the sparse index does not cover can omit a null class
            // default. Match the generated accessor's default without hiding
            // missing required fields or evaluating an initializer out of
            // context.
            if (MemberValueFactory.IsLiteralNullClassDefault(member))
            {
                return null;
            }
            return DispatchMatchedNoValue;
        }

        /// <summary>
        /// One Class's schema entry for a member-read site. Like
        /// <see cref="CallSiteTarget"/>, a site chains one target per receiver
        /// Class and holds while the client's schema resolution does.
        /// </summary>
        internal sealed class MemberSiteTarget
        {
            internal readonly object schemaResolution;
            internal readonly NeoClassNode classNode;
            internal readonly string schemaKey;
            internal readonly MergedSchemaEntry? entry;
            internal readonly MemberSiteTarget? next;
            internal readonly int count;

            internal MemberSiteTarget(
                object schemaResolution,
                NeoClassNode classNode,
                string schemaKey,
                MergedSchemaEntry? entry,
                MemberSiteTarget? next)
            {
                this.schemaResolution = schemaResolution;
                this.classNode = classNode;
                this.schemaKey = schemaKey;
                this.entry = entry;
                this.next = next;
                count = (next?.count ?? 0) + 1;
            }
        }

        /// <summary>
        /// The member <paramref name="schemaKey"/> names on a row of
        /// <paramref name="classId"/>; null when it has none or its class
        /// inherits circularly. Its own method so the exception region stays
        /// off the caller's frame.
        /// </summary>
        /// <param name="rowRef">The row's reference, when there is one; it keeps the class node.</param>
        private static MergedSchemaEntry? SurfaceRowMember(
            RowReference? rowRef,
            string classId,
            string schemaKey,
            ISchemaResolutionSite? site,
            Context ctx)
        {
            try
            {
                NeoClassNode classNode = rowRef is not null
                    ? rowRef.ClassNode(ctx.client, classId)
                    : ctx.client.ResolveClassNode(classId);
                return SurfaceMember(classNode, schemaKey, site, ctx);
            }
            catch (CircularInheritanceError)
            {
                return null;
            }
        }

        /// <param name="site">A <see cref="KeyOf"/> or <see cref="CallGetterPointer"/>, whose targets cache the entry.</param>
        private static MergedSchemaEntry? SurfaceMember(
            NeoClassNode classNode,
            string schemaKey,
            ISchemaResolutionSite? site,
            Context ctx)
        {
            if (site is null)
                return classNode.SurfaceMember(schemaKey);
            ref MemberSiteTarget? siteTargets = ref site is CallGetterPointer getter
                ? ref getter.resolvedMembers
                : ref ((KeyOf)site).resolvedMembers;
            MemberSiteTarget? targets = siteTargets;
            if (targets is not null
                && !ReferenceEquals(targets.schemaResolution, ctx.client.SchemaResolution))
            {
                targets = null;
            }
            for (MemberSiteTarget? target = targets; target is not null; target = target.next)
            {
                if (ReferenceEquals(target.classNode, classNode)
                    && SameId(target.schemaKey, schemaKey))
                {
                    return target.entry;
                }
            }
            MergedSchemaEntry? entry = classNode.SurfaceMember(schemaKey);
            if ((targets?.count ?? 0) < CallSiteTarget.MaxTargets)
            {
                siteTargets = new MemberSiteTarget(
                    ctx.client.SchemaResolution,
                    classNode,
                    schemaKey,
                    entry,
                    targets);
                if (targets is null)
                    ctx.client.RememberSchemaResolutionSite(site);
            }
            return entry;
        }

        /// <summary>
        /// The member <paramref name="key"/> names on a row of
        /// <paramref name="classId"/>, for an access through
        /// <paramref name="site"/> that is not a dispatched read (a write
        /// target). The entry is cached on the site per Class, exactly as
        /// a read's is.
        /// </summary>
        /// <param name="receiver">The row's evaluated object, when the caller has it; its reference keeps the class node.</param>
        internal static bool TryResolveSurfaceMember(
            KeyOf site,
            object? receiver,
            string classId,
            string key,
            Context ctx,
            out JsonMember? member,
            out MergedSchemaEntry? entry)
        {
            member = null;
            entry = SurfaceRowMember(FindRowReference(receiver, ctx), classId, key, site, ctx);
            if (entry is null)
                return false;
            // The class node resolved the entry's authored member; only a
            // variant target member needs the client's lookup.
            member = entry.member;
            return member is not null || ctx.client.TryGetMember(entry.memberId, out member);
        }

        /// <summary>
        /// The node of the stored child a member read of
        /// <paramref name="entry"/> on <paramref name="receiver"/> remembered,
        /// or null.
        /// </summary>
        internal static NeoValueNode? RememberedChildNode(object? receiver, MergedSchemaEntry entry)
        {
            if (receiver is not NeoObjectRecord record)
                return null;
            int slot = record.StoredSlot(entry);
            return slot >= 0 ? record.StoredNode(slot) : null;
        }

        private static object? ReadOnlyDeclarationDefault(
            JsonMember member,
            Context ctx)
        {
            MemberValue? synthetic = ctx.client.ReadOnlyDeclarationDefault(member);
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
                && lookup.Selection != NeoMemberSelectionKind.Multi
                && unwrapped is object?[] selections
                && selections.Length == 1
                && selections[0] is string selectedId)
            {
                return ResolveValueIfId(selectedId, ctx,
                    ResolveLookupSelectionOwnership(ctx, lookup, selectedId));
            }
            return unwrapped;
        }

        /// <summary>
        /// Cycle-checked recursive evaluation of an NSProperty member by id.
        /// The compiled getter is already projected through its sparse
        /// override chain, including authored-code null clears.
        /// </summary>
        /// <param name="getter">The member's compiled getter when the caller already resolved it.</param>
        /// <param name="receiverRef">The receiver's row reference when the caller already found it.</param>
        private static object? DispatchNSGetterById(
            string memberId,
            object? receiver,
            Context ctx,
            FunctionWithReturnType? getter = null,
            RowReference? receiverRef = null)
        {
            if (ContainsFrame(ctx.getterCallStack, memberId))
            {
                throw new NSGetterRuntimeError(
                    $"Circular getter call: member '{memberId}' is already being evaluated");
            }
            getter ??= ResolveCompiledGetter(memberId, ctx.client);
            if (getter is null)
            {
                string name = ctx.client.TryGetMember(memberId, out JsonMember? member)
                    ? member.name
                    : memberId;
                throw new NSGetterRuntimeError(
                    $"Getter '{name}' has no compiled `getter` — save its code to compile it");
            }
            NeoClient client = ctx.client;
            bool memoize = client.CanMemoizeGetters
                && receiver is not NeoScriptObject { attachedId: null }
                && (receiverRef ??= FindRowReference(receiver, ctx)) is not null;
            bool holdsValuelessReads = false;
            if (memoize)
            {
                // The row reference's own slot usually answers, so the
                // memo's key, whose two strings each cost a write barrier
                // to store, is only built where the memo itself is read.
                NeoClient.GetterMemoEntry? hit = receiverRef!.MemoizedGetter(memberId, ctx.valueOwnership);
                if (hit is not null && !client.HoldsCurrentReads(hit))
                    hit = null;
                if (hit is null && (hit = client.FindMemoizedGetter(GetterMemoKeyOf(receiverRef, memberId, ctx), out holdsValuelessReads)) is not null)
                    receiverRef.RememberGetter(memberId, ctx.valueOwnership, hit);
                if (hit is not null)
                {
                    if (hit.list is not null)
                    {
                        if (ResolveMemoizedList(hit.list, hit.listEntryMember, ctx, lendingMemberId: memberId) is { } hitList)
                        {
                            client.ReplayGetterReads(hit);
                            return hitList;
                        }
                        client.ForgetMemoizedGetter(GetterMemoKeyOf(receiverRef, memberId, ctx));
                    }
                    else if (hit.row is null)
                    {
                        client.ReplayGetterReads(hit);
                        return hit.scalar;
                    }
                    else
                    {
                        RowReference hitRef = hit.row;
                        if (client.ReadReplayReference(hitRef.valueId, ref hitRef.node, hitRef.ownership) is { } hitRow)
                        {
                            client.ReplayGetterReads(hit);
                            return UnwrapCached(hitRow, ctx, hitRef.ownership, hitRef.member, hitRef.node);
                        }
                        client.ForgetMemoizedGetter(GetterMemoKeyOf(receiverRef, memberId, ctx));
                    }
                }
            }
            // A live valueless entry already holds what this evaluation reads.
            if (holdsValuelessReads)
                memoize = false;
            int frame = ctx.EnterGetter(memberId, receiver);
            if (!memoize)
            {
                try
                {
                    return Evaluate(getter, ctx, Array.Empty<object?>(), frame);
                }
                finally
                {
                    ctx.ExitFunction(frame);
                }
            }
            NeoClient.GetterCaptureFrame enclosingCapture = client.BeginGetterReadCapture();
            object? result;
            NeoClient.GetterCaptureFrame capture;
            try
            {
                result = Evaluate(getter, ctx, Array.Empty<object?>(), frame);
            }
            finally
            {
                ctx.ExitFunction(frame);
                capture = client.EndGetterReadCapture(enclosingCapture);
            }
            NeoClient.GetterMemoEntry? memoized = null;
            NeoClient.GetterMemoKey memoKey = GetterMemoKeyOf(receiverRef!, memberId, ctx);
            if (!client.CanMemoizeGetters)
                client.RecycleGetterCapture(capture);
            else if (result is null or string or bool or double or int or long or float)
                memoized = client.MemoizeGetter(memoKey, result, null, capture);
            else if (result is NeoScriptObject { attachedId: null } constructed)
            {
                constructed.ShareGetterResult();
                memoized = client.MemoizeGetter(memoKey, constructed, null, capture);
            }
            else if (FindRowReference(result, ctx) is { } resultRef)
            {
                client.ShareConstructedGetterRow(resultRef, result, ctx, capture);
                memoized = client.MemoizeGetter(memoKey, null, resultRef, capture);
            }
            else if (result is object?[] entries
                && MemoizableList(entries, ctx, out JsonMember? entryMember) is { } list)
                memoized = client.MemoizeGetter(memoKey, null, null, capture, list, entryMember);
            else
                client.RecycleGetterCapture(capture);
            if (memoized is not null)
                receiverRef!.RememberGetter(memberId, ctx.valueOwnership, memoized);
            return result;
        }

        private static NeoClient.GetterMemoKey GetterMemoKeyOf(RowReference receiverRef, string memberId, Context ctx) =>
            new(receiverRef.ownership, receiverRef.valueId, memberId, ctx.valueOwnership);

        internal static bool ContainsFrame(IReadOnlyCollection<string> stack, string memberId)
        {
            if (stack is Context.CallFrameStack pushed)
                return pushed.Count != 0 && pushed.Contains(memberId);
            if (stack is IReadOnlyList<string> frames)
            {
                for (int i = 0; i < frames.Count; i++)
                    if (frames[i] == memberId)
                        return true;
                return false;
            }
            return stack.Contains(memberId);
        }

        private static FunctionWithReturnType? ResolveCompiledGetter(
            string memberId, NeoClient client)
        {
            return client.TryGetMember(memberId, out NSPropertyMember? property)
                ? property.getter
                : null;
        }

        // ---------------------------------------------------------------
        // Operations
        // ---------------------------------------------------------------

        private static readonly object BoxedTrue = true;
        private static readonly object BoxedFalse = false;

        internal static object Box(bool value) => value ? BoxedTrue : BoxedFalse;

        // A bool? boxes a new object; a stored bool reads as a shared box.
        internal static object? Box(bool? value) => value is bool set ? Box(set) : null;

        internal static object Box(int value) => NeoNumbers.Box(value);

        internal static object Box(float value) => NeoNumbers.Box(value);

        internal static object Box(double value) => NeoNumbers.Box(value);

        private static object? EvalOperation(
            Operation operation,
            NeoScriptScope scope,
            Context ctx)
        {
            switch (operation)
            {
                case ArithmeticOperation arith:
                    {
                        object? value = EvalArithmetic(arith.arithmetic, scope, ctx, out double number);
                        return ArithmeticValue.Box(value, number);
                    }
                case BooleanOperation boolOp:
                    return Box(EvalBooleanExpression(boolOp.expression, scope, ctx));
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown operation kind {operation.GetType().Name}");
            }
        }

        // Numeric intermediates remain values on the C# stack. Only crossing
        // back into the reference-valued interpreter requires a box. Strings,
        // decimal math and mixed operands retain the shared conversion path.
        // The hot path carries a value as a returned reference plus an `out`
        // double: <see cref="ArithmeticValue.BareNumber"/> when the double
        // holds it, otherwise the value itself. Neither is a heap store, where
        // a struct's reference field is write-barriered even on the stack.
        internal readonly struct ArithmeticValue
        {
            // The reference a bare number carries.
            internal static readonly object BareNumber = new();

            private readonly double number;
            private readonly object? reference;
            internal ArithmeticValue(object? reference, double number)
            {
                this.number = number;
                this.reference = reference;
            }
            // Most values are not numbers: a boxed one converts when read.
            internal ArithmeticValue(object? value)
            {
                number = 0;
                reference = value;
            }
            internal object? Reference => reference;
            internal double Number => NumberOf(reference, number);
            internal bool IsNumber => IsNumeric(reference);
            internal object? Box() => Box(reference, number);

            /// <summary>The value as a number; 0 when <see cref="IsNumeric"/> is false.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static double NumberOf(object? reference, double number) =>
                ReferenceEquals(reference, BareNumber) ? number : BoxedNumber(reference);
            private static double BoxedNumber(object? value)
            {
                TryAsDouble(value, out double converted);
                return converted;
            }
            /// <summary>Whether <see cref="NumberOf"/> holds the value: <see cref="TryAsDouble"/>'s types.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool IsNumeric(object? reference) =>
                ReferenceEquals(reference, BareNumber)
                || reference is ValueType and (double or float or int or long or short or decimal);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static object? Box(object? reference, double number) =>
                ReferenceEquals(reference, BareNumber) ? NSGetterEvaluator.Box(number) : reference;
        }

        /// <returns><see cref="ArithmeticValue.BareNumber"/> when <paramref name="number"/> holds the value.</returns>
        internal static object? EvaluateValue(Pointer pointer, NeoScriptScope scope, Context ctx, out double number)
        {
            // A local is the commonest operand.
            if (pointer is VariablePointer variable)
            {
                if (scope.TryReadNumber(variable, out number))
                    return ArithmeticValue.BareNumber;
            }
            else if (pointer is ValuePointer { primitiveResolved: true } literal)
            {
                number = 0;
                return literal.primitive;
            }
            else if (pointer is OperationPointer { operation: ArithmeticOperation arithmetic })
                return EvalArithmetic(arithmetic.arithmetic, scope, ctx, out number);
            else if (pointer is FunctionPointer { function: MathOpFunction math })
                return EvalMathOp(math.info, scope, ctx, out number);
            // Non-numeric reads retain row-alias refresh and all ordinary
            // interpreter semantics at the shared pointer boundary.
            number = 0;
            return EvalPointer(pointer, scope, ctx);
        }

        /// <summary>
        /// <see cref="EvaluateValue"/> for an arithmetic or comparison
        /// operand: a local's number or a numeric literal reads without its
        /// frame.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? EvaluateOperand(Pointer pointer, NeoScriptScope scope, Context ctx, out double number)
        {
            if (pointer is VariablePointer variable)
            {
                if (scope.TryReadNumber(variable, out number))
                    return ArithmeticValue.BareNumber;
                return EvalPointer(pointer, scope, ctx);
            }
            if (pointer is ValuePointer { primitiveIsNumber: true } literal)
            {
                number = literal.primitiveNumber;
                return ArithmeticValue.BareNumber;
            }
            return EvaluateValue(pointer, scope, ctx, out number);
        }

        private static object? EvalArithmetic(ArithmeticOpInfo info, NeoScriptScope scope, Context ctx, out double number)
        {
            number = 0;
            if (info.pointers.Length == 2 && info.isDecimal != true)
            {
                object? left = EvaluateOperand(info.pointers[0], scope, ctx, out double leftNumber);
                object? right = EvaluateOperand(info.pointers[1], scope, ctx, out double rightNumber);
                if (ArithmeticValue.IsNumeric(left) && ArithmeticValue.IsNumeric(right))
                {
                    number = ApplyNumericArithmetic(
                        info.type,
                        ArithmeticValue.NumberOf(left, leftNumber),
                        ArithmeticValue.NumberOf(right, rightNumber),
                        info.numeric);
                    return ArithmeticValue.BareNumber;
                }
                // The usual non-numeric +: a string joins its two parts
                // without an operand array or a builder.
                if (info.type == ArithmeticOpKind.Addition && (left is string || right is string))
                {
                    return string.Concat(
                        StringifyOperand(left, leftNumber),
                        StringifyOperand(right, rightNumber));
                }
                return ApplyArithmetic(
                    info.type,
                    new[] { ArithmeticValue.Box(left, leftNumber), ArithmeticValue.Box(right, rightNumber) },
                    false,
                    info.numeric,
                    ctx);
            }
            return EvalArithmeticOperands(info, scope, ctx, out number);
        }

        // Its own method so the pool's exception region stays off the
        // two-operand path's frame.
        private static object? EvalArithmeticOperands(ArithmeticOpInfo info, NeoScriptScope scope, Context ctx, out double number)
        {
            number = 0;
            // Preserve evaluation order: evaluate every operand before folding,
            // including when an earlier division will subsequently fail.
            var operands = System.Buffers.ArrayPool<ArithmeticValue>.Shared.Rent(info.pointers.Length);
            try
            {
                bool numeric = info.isDecimal != true && info.pointers.Length > 0;
                for (int i = 0; i < info.pointers.Length; i++)
                {
                    object? operand = EvaluateValue(info.pointers[i], scope, ctx, out double operandNumber);
                    operands[i] = new ArithmeticValue(operand, operandNumber);
                    numeric &= ArithmeticValue.IsNumeric(operand);
                }
                if (numeric)
                {
                    double result = operands[0].Number;
                    if (info.pointers.Length == 1)
                    {
                        if (!IsArithmeticOp(info.type))
                            throw new NSGetterRuntimeError($"Unknown arithmetic op '{info.type}'");
                        if (info.type == ArithmeticOpKind.Addition)
                            result += 0d;
                    }
                    for (int i = 1; i < info.pointers.Length; i++)
                        result = ApplyNumericArithmetic(info.type, result, operands[i].Number, info.numeric);
                    number = result;
                    return ArithmeticValue.BareNumber;
                }
                var boxed = new object?[info.pointers.Length];
                for (int i = 0; i < boxed.Length; i++)
                    boxed[i] = operands[i].Box();
                return ApplyArithmetic(info.type, boxed, info.isDecimal == true, info.numeric, ctx);
            }
            finally
            {
                System.Array.Clear(operands, 0, info.pointers.Length);
                System.Buffers.ArrayPool<ArithmeticValue>.Shared.Return(operands);
            }
        }

        private static object? ApplyArithmetic(
            string op,
            object?[] operands,
            bool isDecimal,
            ArithmeticNumeric? numeric,
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
            // `+` with any string operand concatenates every operand.
            if (op == ArithmeticOpKind.Addition)
            {
                foreach (var o in operands)
                {
                    if (o is string)
                        return Concatenate(operands);
                }
            }
            // Numeric path. Coerce every operand to double; ints round-trip.
            double folded = ToArithmeticOperand(operands[0]);
            if (operands.Length == 1)
            {
                if (!IsArithmeticOp(op))
                    throw new NSGetterRuntimeError($"Unknown arithmetic op '{op}'");
                if (op == ArithmeticOpKind.Addition)
                    folded += 0d;
            }
            for (int i = 1; i < operands.Length; i++)
            {
                folded = ApplyNumericArithmetic(op, folded, ToArithmeticOperand(operands[i]), numeric);
            }
            return folded;
        }

        private static double ToArithmeticOperand(object? operand)
        {
            if (!TryAsDouble(operand, out double d))
            {
                throw new NSGetterRuntimeError(
                    $"Arithmetic operand is not numeric: {ReceiverTypeName(operand)}");
            }
            return d;
        }

        private static bool IsArithmeticOp(string op) =>
            op == ArithmeticOpKind.Addition
            || op == ArithmeticOpKind.Subtraction
            || op == ArithmeticOpKind.Multiplication
            || op == ArithmeticOpKind.Division
            || op == ArithmeticOpKind.Remainder;

        private static double ApplyNumericArithmetic(string op, double left, double right, ArithmeticNumeric? numeric)
        {
            // Every ArithmeticOpKind is one character. Switching on it is a
            // jump table; a string switch compares the op to each case in turn.
            switch (op.Length == 1 ? op[0] : '\0')
            {
                // The TS evaluator folds addition from a 0 seed, which turns a
                // -0 sum into +0; the trailing 0 keeps that parity.
                case '+':
                    return RoundArithmetic(left + right + 0d, numeric);
                case '-':
                    return RoundArithmetic(left - right, numeric);
                case '*':
                    return RoundArithmetic(left * right, numeric);
                case '/':
                    if (right == 0)
                        throw new NSGetterRuntimeError("Division by zero");
                    if (numeric == ArithmeticNumeric.Int)
                        return System.Math.Truncate(left / right);
                    return RoundArithmetic(left / right, numeric);
                case '%':
                    if (right == 0)
                        throw new NSGetterRuntimeError("Modulo by zero");
                    return RoundArithmetic(left % right, numeric);
                default:
                    throw new NSGetterRuntimeError($"Unknown arithmetic op '{op}'");
            }
        }

        /// <summary>A float step rounds to 32 bits, as Unity's own float math does.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double RoundArithmetic(double value, ArithmeticNumeric? numeric)
        {
            return numeric == ArithmeticNumeric.Float ? (float)value : value;
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
                if (!NeoNumbers.IsWhole(number))
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
                            for (int i = 1; i < decimals.Length; i++)
                                acc = NeoDecimalMath.Add(acc, decimals[i]);
                            return acc;
                        }
                    case ArithmeticOpKind.Subtraction:
                        {
                            string acc = decimals[0];
                            for (int i = 1; i < decimals.Length; i++)
                                acc = NeoDecimalMath.Subtract(acc, decimals[i]);
                            return acc;
                        }
                    case ArithmeticOpKind.Multiplication:
                        {
                            string acc = decimals[0];
                            for (int i = 1; i < decimals.Length; i++)
                                acc = NeoDecimalMath.Multiply(acc, decimals[i]);
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

        internal static bool EvalBooleanExpression(
            BooleanExpression expression,
            NeoScriptScope scope,
            Context ctx)
        {
            bool head = EvalCondition(expression.condition, scope, ctx);
            if (expression.connective is null)
                return head;
            // The first character tells the two LogicalOpKinds apart, without
            // comparing the operator string to each.
            string? type = expression.connective.type;
            switch (type is { Length: 2 } ? type[0] : '\0')
            {
                case '&':
                    return head && EvalBooleanExpression(expression.connective.to, scope, ctx);
                case '|':
                    return head || EvalBooleanExpression(expression.connective.to, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown logical operator '{expression.connective.type}'");
            }
        }

        /// <summary>
        /// A comparison only reads its operands, so an array literal (an enum
        /// value, say) is unwrapped once per pointer instead of per evaluation.
        /// Elsewhere every evaluation must yield a fresh array: array identity
        /// carries list provenance.
        /// </summary>
        private static object? EvaluateComparand(Pointer pointer, NeoScriptScope scope, Context ctx, out double number)
        {
            if (pointer is ValuePointer { primitiveResolved: false, value: { value: JArray items } literal } vp
                && literal.typeInfo.type != MemberKind.NSAction)
            {
                number = 0;
                return vp.comparand ??= UnwrapJToken(items);
            }
            return EvaluateOperand(pointer, scope, ctx, out number);
        }

        private static bool EvalCondition(
            Condition condition,
            NeoScriptScope scope,
            Context ctx)
        {
            ComparisonOp op = ComparisonOf(condition);
            object? left = EvaluateComparand(condition.operand1, scope, ctx, out double leftNumber);
            object? right = EvaluateComparand(condition.operand2, scope, ctx, out double rightNumber);
            if (condition.isDecimal != true && ArithmeticValue.IsNumeric(left) && ArithmeticValue.IsNumeric(right))
            {
                // Keep the existing subtraction-based ordering, including
                // its NaN/infinity behavior, without boxing either operand.
                leftNumber = ArithmeticValue.NumberOf(left, leftNumber);
                rightNumber = ArithmeticValue.NumberOf(right, rightNumber);
                double difference = leftNumber - rightNumber;
                switch (op)
                {
                    case ComparisonOp.EqualTo:
                        return leftNumber == rightNumber;
                    case ComparisonOp.DoesNotEqual:
                        return leftNumber != rightNumber;
                    case ComparisonOp.GreaterThan:
                        return difference > 0;
                    case ComparisonOp.GreaterThanOrEqualTo:
                        return difference >= 0;
                    case ComparisonOp.LessThan:
                        return difference < 0;
                    case ComparisonOp.LessThanOrEqualTo:
                        return difference <= 0;
                }
            }
            var a = ArithmeticValue.Box(left, leftNumber);
            var b = ArithmeticValue.Box(right, rightNumber);
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
                    switch (op)
                    {
                        case ComparisonOp.EqualTo:
                            return aIsNull && bIsNull;
                        case ComparisonOp.DoesNotEqual:
                            return !(aIsNull && bIsNull);
                        default:
                            throw new NSGetterRuntimeError(
                                "Decimal ordering comparison received a null operand.");
                    }
                }
                int comparison = NeoDecimalMath.Compare(
                    CoerceDecimalOperand(a, "comparison"),
                    CoerceDecimalOperand(b, "comparison"));
                switch (op)
                {
                    case ComparisonOp.EqualTo:
                        return comparison == 0;
                    case ComparisonOp.DoesNotEqual:
                        return comparison != 0;
                    case ComparisonOp.GreaterThan:
                        return comparison > 0;
                    case ComparisonOp.GreaterThanOrEqualTo:
                        return comparison >= 0;
                    case ComparisonOp.LessThan:
                        return comparison < 0;
                    case ComparisonOp.LessThanOrEqualTo:
                        return comparison <= 0;
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown comparison operator '{condition.type}'");
                }
            }
            switch (op)
            {
                case ComparisonOp.EqualTo:
                    return condition.reference ? ReferenceEqual(a, b, ctx) : JsEqual(a, b);
                case ComparisonOp.DoesNotEqual:
                    return condition.reference ? !ReferenceEqual(a, b, ctx) : !JsEqual(a, b);
                case ComparisonOp.GreaterThan:
                    return NumericCompare(a, b) > 0;
                case ComparisonOp.GreaterThanOrEqualTo:
                    return NumericCompare(a, b) >= 0;
                case ComparisonOp.LessThan:
                    return NumericCompare(a, b) < 0;
                case ComparisonOp.LessThanOrEqualTo:
                    return NumericCompare(a, b) <= 0;
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown comparison operator '{condition.type}'");
            }
        }

        // The operator parsed once per condition: a string switch compares
        // it to each case in turn on every evaluation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ComparisonOp ComparisonOf(Condition condition)
        {
            if (condition.comparison != ComparisonOp.Unresolved)
                return condition.comparison;
            return condition.comparison = condition.type switch
            {
                OperatorKind.EqualTo => ComparisonOp.EqualTo,
                OperatorKind.DoesNotEqual => ComparisonOp.DoesNotEqual,
                OperatorKind.GreaterThan => ComparisonOp.GreaterThan,
                OperatorKind.GreaterThanOrEqualTo => ComparisonOp.GreaterThanOrEqualTo,
                OperatorKind.LessThan => ComparisonOp.LessThan,
                OperatorKind.LessThanOrEqualTo => ComparisonOp.LessThanOrEqualTo,
                _ => ComparisonOp.Unknown,
            };
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
            NeoScriptScope scope,
            Context ctx)
        {
            NeoGeneratedTypesSupport.ConstructionSiteBuffers buffers =
                info.buffers ??= new NeoGeneratedTypesSupport.ConstructionSiteBuffers();
            if (!buffers.TryRent())
                return EvalDeclaredConstructor(info, null, scope, ctx);
            try
            {
                return EvalDeclaredConstructor(info, buffers, scope, ctx);
            }
            finally
            {
                buffers.Return();
            }
        }

        /// <param name="buffers">The site's rented buffers, or null to build fresh ones.</param>
        private static object? EvalDeclaredConstructor(
            DeclaredConstructorInfo info,
            NeoGeneratedTypesSupport.ConstructionSiteBuffers? buffers,
            NeoScriptScope scope,
            Context ctx)
        {
            IReadOnlyList<NeoGeneratedTypesSupport.RuntimeConstructorField> fields =
                buffers?.Fields(info.fields)
                ?? NeoGeneratedTypesSupport.ConstructionSiteBuffers.NewFields(info.fields);
            ctx.TryGetConstructionClassContext(
                info.schemaClassInfo.classId,
                out IReadOnlyDictionary<string, GenericBinding>?
                    replayClassArguments,
                out IReadOnlyDictionary<string, string>?
                    replayGenericBindings);
            bool cacheable = replayClassArguments is null && replayGenericBindings is null;
            if (!cacheable
                || !NeoGeneratedTypesSupport.TryGetResolvedSite(
                    ctx.client, info, ref info.resolvedSite, out NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved))
            {
                var argumentNames = new List<string>(info.args.Length);
                foreach (DeclaredConstructorArgument argument in info.args)
                {
                    argumentNames.Add(argument.name);
                }
                try
                {
                    resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(
                        ctx.client,
                        info.schemaClassInfo,
                        info.constructorId,
                        argumentNames,
                        fields,
                        replayClassArguments,
                        replayGenericBindings);
                }
                catch (Exception error)
                    when (error is InvalidOperationException
                        || error is ArgumentException)
                {
                    throw new NSGetterRuntimeError(
                        $"Declared constructor failed: {error.Message}");
                }
                if (cacheable)
                    NeoGeneratedTypesSupport.CacheResolvedSite(ctx.client, info, ref info.resolvedSite, resolved);
            }

            object?[] argumentValues = buffers?.Arguments(resolved) ?? resolved.NewArgumentValues();
            for (int i = 0; i < info.args.Length; i++)
            {
                argumentValues[resolved.argumentPositions[i]] = EvalPointer(
                    info.args[i].valuePointer,
                    scope,
                    ctx);
            }

            try
            {
                return ConstructDeclared(
                    resolved,
                    argumentValues,
                    fields,
                    ctx,
                    fields.Count == 0 ? null : FieldValueEvaluator(info, fields, scope),
                    replayClassArguments is not null || replayGenericBindings is not null);
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
        /// P43 §6.1 step 4 — the call-site initializer block is evaluated
        /// AFTER the body, as in C# where an object initializer's expressions
        /// run once the constructor has returned. Handing construction a thunk
        /// instead of pre-evaluated values is what keeps that order:
        /// evaluating earlier would make a field expression read pre-body
        /// state. Its own method because C# allocates a closure where its
        /// captures are declared, and most constructions supply no fields.
        /// </summary>
        private static Action<Context> FieldValueEvaluator(
            DeclaredConstructorInfo info,
            IReadOnlyList<NeoGeneratedTypesSupport.RuntimeConstructorField> fields,
            NeoScriptScope scope) => constructionCtx =>
        {
            for (int i = 0; i < fields.Count; i++)
            {
                fields[i].value = EvalPointer(
                    info.fields[i].valuePointer,
                    scope,
                    constructionCtx);
            }
        };

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

        /// <summary>
        /// P67 §4.1 through the IR. Thin dispatch into
        /// <see cref="NeoVariantSupport"/> so the typed
        /// <see cref="NeoVariant{T}"/> handle and the evaluator run one
        /// implementation of the construction order, not two.
        /// </summary>
        private static object? EvalVariantInitialize(
            FunctionVariantInitializeInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            NeoVariantReference reference = RequireVariantReference(
                EvalPointer(info.variantPointer, scope, ctx),
                "Initialize");
            try
            {
                VariantRecord? record = NeoVariantSupport.ResolveRecord(
                    ctx.client,
                    reference.classId,
                    reference.variantId);
                (object? lookupRow, string? lookupRowValueId) =
                    ResolveVariantLookupRow(
                        info.rowPointer,
                        reference.rowValueId,
                        scope,
                        ctx);
                NeoMemberClassWritable node = NeoVariantSupport.InitializeNode(
                    ctx.client,
                    reference.classId,
                    record,
                    lookupRow,
                    lookupRowValueId);
                MemberValue? row = node.value;
                if (row is null)
                {
                    throw new NSGetterRuntimeError(
                        "Variant Initialize produced no backing row.");
                }
                ctx.allocationTracker.RegisterSessionRoot(row.id);
                return UnwrapCached(row, ctx, NeoValueOwnership.Session, node.member);
            }
            catch (Exception error)
                when (error is InvalidOperationException
                    || error is ArgumentException)
            {
                throw new NSGetterRuntimeError(
                    $"Variant Initialize failed: {error.Message}");
            }
        }

        private static bool EvalTileConvert(
            TileConvertPointer pointer,
            NeoScriptScope scope,
            Context ctx)
        {
            object? receiver = EvalPointer(pointer.receiverPointer, scope, ctx);
            if (receiver is null || FindRowReference(receiver, ctx) is not { } source)
                throw new NSGetterRuntimeError("Tile conversion receiver has no backing value row.");
            if (ctx.allocationTracker.IsAllocatedSessionRoot(source.valueId))
                return false;
            string? targetClassId = pointer.targetClassId;
            if (pointer.targetPointer is not null)
            {
                object? target = EvalPointer(pointer.targetPointer, scope, ctx);
                if (target is null || FindRowReference(target, ctx) is not { } targetRow)
                    throw new NSGetterRuntimeError("Tile conversion target has no backing class row.");
                targetClassId = ctx.client.TryGetValue(targetRow.ownership, targetRow.valueId, out ObjectMemberValue? targetValue)
                    ? targetValue.classId : null;
            }
            if (string.IsNullOrWhiteSpace(targetClassId))
                throw new NSGetterRuntimeError("Tile conversion target has no class.");
            try
            {
                ctx.client.ConvertTile(source.ownership, source.valueId, targetClassId!);
                if (ctx.client.TryGetValue(source.ownership, source.valueId, out ObjectMemberValue? converted))
                    RefreshCachedRowAfterWrite(converted, ctx, source.ownership);
                return true;
            }
            catch (NeoPlacementValidationException)
            {
                return false;
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
            {
                throw new NSGetterRuntimeError($"Tile conversion failed for value '{source.valueId}': {error.Message}");
            }
        }

        /// <summary>
        /// P67 §4.2 through the IR. Application is in place, so the value is
        /// the receiver expression's own — re-unwrapped rather than rebuilt,
        /// because the rows the application wrote have changed underneath it.
        /// </summary>
        private static object? EvalVariantApply(
            FunctionVariantApplyInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            object? receiver = EvalPointer(info.receiverPointer, scope, ctx);
            if (receiver is null)
            {
                throw new NSGetterRuntimeError(
                    "ToVariant receiver is null; narrow or force-unwrap the optional value first.");
            }
            if (FindRowReference(receiver, ctx) is not { } source)
            {
                throw new NSGetterRuntimeError(
                    "ToVariant receiver has no backing value row.");
            }
            NeoVariantReference reference = RequireVariantReference(
                EvalPointer(info.variantPointer, scope, ctx),
                "ToVariant");
            try
            {
                VariantRecord? record = NeoVariantSupport.ResolveRecord(
                    ctx.client,
                    reference.classId,
                    reference.variantId);
                (object? lookupRow, string? lookupRowValueId) =
                    ResolveVariantLookupRow(
                        info.rowPointer,
                        reference.rowValueId,
                        scope,
                        ctx);
                if (!ctx.client.TryGetValue(
                        source.ownership,
                        source.valueId,
                        out ObjectMemberValue? row))
                {
                    throw new NSGetterRuntimeError(
                        $"ToVariant receiver row '{source.valueId}' could not be read.");
                }
                var member = new ClassMember
                {
                    id = $"__neo_variant_target_{source.valueId}",
                    name = "VariantTarget",
                    kind = MemberKind.Class,
                    classId = row.classId ?? source.classId ?? string.Empty,
                    createdAt = row.createdAt,
                    updatedAt = row.updatedAt,
                };
                // This view shares cached descendants with the caller's object.
                // Keep it in the client graph; disposing a temporary root also
                // disposes the live object's state after ToVariant returns.
                var node = (NeoMemberClassWritable)NeoMember.CreateWritable(
                    ctx.client,
                    member,
                    source.valueId,
                    source.ownership);
                NeoVariantSupport.ApplyToNode(
                    ctx.client,
                    record,
                    node,
                    source.ownership,
                    lookupRow,
                    lookupRowValueId,
                    freshlyConstructed: ctx.allocationTracker.IsAllocatedSessionRoot(source.valueId));
                MemberValue? applied = node.value;
                if (applied is not null)
                {
                    // The application wrote through the node, so the receiver's
                    // cached unwrapped shape is stale.
                    RefreshCachedRowAfterWrite(applied, ctx, source.ownership);
                }
                // §4.2 step 4: the value is the receiver itself, never a
                // replacement — so hand back the very object the receiver
                // expression produced.
                return receiver;
            }
            catch (Exception error)
                when (error is InvalidOperationException
                    || error is ArgumentException)
            {
                throw new NSGetterRuntimeError(
                    $"ToVariant failed for value '{source.valueId}': {error.Message}",
                    error);
            }
        }

        private static NeoVariantReference RequireVariantReference(
            object? value,
            string usage)
        {
            if (value is NeoVariantReference reference)
                return reference;
            throw new NSGetterRuntimeError(
                $"{usage} expected a NeoVariant value, got '{value?.GetType().Name ?? "null"}'.");
        }

        private static (object? value, string? valueId) ResolveVariantLookupRow(
            Pointer? explicitPointer,
            string? boundRowValueId,
            NeoScriptScope scope,
            Context ctx)
        {
            if (explicitPointer is not null)
            {
                object? value = EvalPointer(explicitPointer, scope, ctx);
                return (value, ValueIdOf(value, ctx));
            }
            if (string.IsNullOrWhiteSpace(boundRowValueId))
                return (null, null);
            NeoValueOwnership ownership = ResolveOwnershipForValueId(
                ctx,
                boundRowValueId!);
            if (!ctx.client.TryGetValue(
                    ownership,
                    boundRowValueId!,
                    out MemberValue? row))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup variant row '{boundRowValueId}' was not found.");
            }
            return (
                UnwrapCached(row, ctx, ownership),
                boundRowValueId);
        }

        // Each operator body lives in its own method: Mono zeroes a method's
        // whole frame on entry, and one switch holding every case's struct
        // locals made every call pay for all of them.
        private static object? EvalFunction(
            Function fn,
            NeoScriptScope scope,
            Context ctx)
        {
            switch (fn)
            {
                // Hot kinds first: each case is a type test.
                case ListIndexFunction lif:
                    return EvalListIndex(lif.info, scope, ctx);
                case CountFunction cf:
                    return EvalCount(cf, scope, ctx);
                case ContainsFunction cnf:
                    return EvalContains(cnf, scope, ctx);
                case IndexOfFunction iof:
                    return EvalIndexOf(iof, scope, ctx);
                case WhereFunction wf:
                    return EvalWhere(wf, scope, ctx);
                case FirstFunction _:
                case FirstOrDefaultFunction _:
                    return EvalFirst(fn, scope, ctx);
                case SelectFunction sf:
                    return EvalSelect(sf, scope, ctx);
                case MathOpFunction mof:
                    {
                        object? value = EvalMathOp(mof.info, scope, ctx, out double number);
                        return ArithmeticValue.Box(value, number);
                    }
                case ClassConstructorFunction constructor:
                    return EvalClassConstructor(constructor, scope, ctx);
                case DeclaredConstructorFunction declared:
                    return EvalDeclaredConstructor(declared.info, scope, ctx);
                case VariantInitializeFunction variantInitialize:
                    return EvalVariantInitialize(variantInitialize.info, scope, ctx);
                case VariantApplyFunction variantApply:
                    return EvalVariantApply(variantApply.info, scope, ctx);
                case ClassCloneFunction ccf:
                    return EvalClassClone(ccf, scope, ctx);
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
                case ListRepeatFunction lrf:
                    return EvalListRepeat(lrf.info, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown function kind {fn.GetType().Name}");
            }
        }

        private static object? EvalClassConstructor(
            ClassConstructorFunction constructor,
            NeoScriptScope scope,
            Context ctx)
        {
            FunctionClassConstructorInfo info = constructor.info;
            if (info.fields.Length == 0)
            {
                return EvalClassConstructor(
                    constructor,
                    Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(),
                    scope,
                    ctx);
            }
            NeoGeneratedTypesSupport.ConstructionSiteBuffers buffers =
                info.buffers ??= new NeoGeneratedTypesSupport.ConstructionSiteBuffers();
            if (!buffers.TryRent())
            {
                return EvalClassConstructor(
                    constructor,
                    NeoGeneratedTypesSupport.ConstructionSiteBuffers.NewFields(info.fields),
                    scope,
                    ctx);
            }
            try
            {
                return EvalClassConstructor(constructor, buffers.Fields(info.fields), scope, ctx);
            }
            finally
            {
                buffers.Return();
            }
        }

        private static object? EvalClassConstructor(
            ClassConstructorFunction constructor,
            NeoGeneratedTypesSupport.RuntimeConstructorField[] fields,
            NeoScriptScope scope,
            Context ctx)
        {
            ctx.TryGetConstructionClassContext(
                constructor.info.schemaClassInfo.classId,
                out IReadOnlyDictionary<string, GenericBinding>?
                    replayClassArguments,
                out IReadOnlyDictionary<string, string>?
                    replayGenericBindings);
            if (replayClassArguments is not null
                || !NeoGeneratedTypesSupport.TryGetResolvedSite(
                    ctx.client,
                    constructor.info,
                    ref constructor.info.resolvedSite,
                    out NeoGeneratedTypesSupport.RuntimeConstructorMetadata metadata))
            {
                try
                {
                    metadata = NeoGeneratedTypesSupport
                        .ValidateRuntimeClassConstructorMetadata(
                        ctx.client,
                        constructor.info.schemaClassInfo,
                        fields,
                        replayClassArguments);
                }
                catch (Exception error)
                    when (error is InvalidOperationException
                        || error is ArgumentException)
                {
                    throw new NSGetterRuntimeError(
                        $"Class constructor failed: {error.Message}");
                }
                if (replayClassArguments is null)
                    NeoGeneratedTypesSupport.CacheResolvedSite(ctx.client, constructor.info, ref constructor.info.resolvedSite, metadata);
            }
            // P43 §7.2.3 — the schema-derived arm is a construction
            // too, so it opens its own frame before any field runs,
            // exactly where `constructClassValue` opens one in
            // evaluateNSGetter.ts. The framed context is then threaded
            // into the materializer, so a member initializer met while
            // filling defaults counts against the SAME cap instead of
            // starting a fresh stack that can never trip.
            int frame =
                NeoGeneratedTypesSupport.EnterConstructionFrame(
                    ctx,
                    metadata.frameLabel ??= ConstructedClassLabel(
                        ctx,
                        constructor.info.schemaClassInfo.classId),
                    ref metadata.constructionFrames);
            try
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    // Deliberately eval-first, unlike the declared arm:
                    // this IR has no body, so there is nothing for a field
                    // expression to observe, and both runtimes pin the
                    // legacy order here.
                    fields[i].value = EvalPointer(
                        constructor.info.fields[i].valuePointer,
                        scope,
                        ctx);
                }
                try
                {
                    // A plain temporary stays in slots until something
                    // needs its row; see NeoScriptObject.
                    if (replayClassArguments is null
                        && replayGenericBindings is null
                        && !ctx.client.IsReplayingVirtualInstance
                        && !ctx.client.IsPreparingVariant
                        && metadata.DetachedPlan(
                            ctx.client,
                            constructor.info.schemaClassInfo.classId) is { requiresConstructor: false } detachedPlan
                        && NeoGeneratedTypesSupport.CreateDetached(
                            detachedPlan,
                            fields,
                            ctx) is { } detached)
                    {
                        return detached;
                    }
                    NeoGeneratedTypesSupport.RuntimeConstructedClassValue node =
                        NeoGeneratedTypesSupport.CreateRuntimeClassValue(
                            ctx.client,
                            constructor.info.schemaClassInfo,
                            fields,
                            metadata,
                            ConstructorReferences(ctx),
                            ctx);
                    if (replayGenericBindings is not null)
                    {
                        node.value.genericBindings =
                            new Dictionary<string, string>(
                                replayGenericBindings,
                                StringComparer.Ordinal);
                    }
                    ctx.allocationTracker.RegisterSessionRoot(node.value.id);
                    object? unwrapped = UnwrapCached(
                        node.value,
                        ctx,
                        NeoValueOwnership.Session,
                        node.member);
                    return unwrapped;
                }
                catch (Exception error)
                    when (error is InvalidOperationException
                        || error is ArgumentException)
                {
                    throw new NSGetterRuntimeError(
                        $"Class constructor failed: {error.Message}");
                }
            }
            finally
            {
                ctx.ExitFunction(frame);
            }
        }

        private static object? EvalClassClone(
            ClassCloneFunction ccf,
            NeoScriptScope scope,
            Context ctx)
        {
            var receiver = EvalPointer(ccf.info.receiverPointer, scope, ctx);
            if (receiver is null)
            {
                throw new NSGetterRuntimeError(
                    "Class.Clone receiver is null; narrow or force-unwrap the optional value first.");
            }
            if (FindRowReference(receiver, ctx) is not { } source)
            {
                throw new NSGetterRuntimeError(
                    "Class.Clone receiver has no backing value row.");
            }
            try
            {
                string cloneId = ctx.client.CloneValueReference(
                    source.valueId,
                    source.ownership,
                    source.member);
                ctx.allocationTracker.RegisterSessionRoot(cloneId);
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

        private static object? EvalCount(
            CountFunction cf,
            NeoScriptScope scope,
            Context ctx)
        {
            var inner = cf.info.function;
            object? c;
            if (inner is null && ctx.client.PendingScriptWrites is { HasCollections: true })
            {
                if (EvalPendingCollection(cf.info.collectionPointer, PendingRead.Count, scope, ctx, out c) is { } pending)
                {
                    ctx.client.NotePendingRead(pending.Row.id);
                    return Box(pending is NeoWriteBatch.PendingList list
                        ? list.Count
                        : ((ObjectMemberValue)pending.Row).value!.Count);
                }
            }
            else
                c = EvalPointer(cf.info.collectionPointer, scope, ctx);
            if (inner is null)
                return Box(CollectionLength(c));

            bool isList = CollectionIsList(c);
            int count = 0;
            var callback = new PreparedCollectionCallback(
                inner,
                scope,
                ctx,
                isList,
                CollectionCallbackReturnContract.Predicate);
            try
            {
                var cursor = new CollectionCursor(c, ctx);
                while (cursor.MoveNextUnresolved())
                {
                    if (callback.Test(in cursor, cursor.ResolveEntry(ctx)))
                        count++;
                }
                object boxedCount = Box(count);
                callback.CompleteOperator(boxedCount);
                return boxedCount;
            }
            finally
            {
                callback.Dispose();
            }
        }

        private static object? EvalContains(
            ContainsFunction cnf,
            NeoScriptScope scope,
            Context ctx)
        {
            object? c;
            NeoWriteBatch.PendingCollection? pending = null;
            if (ctx.client.PendingScriptWrites is { HasCollections: true })
                pending = EvalPendingCollection(cnf.info.collectionPointer, PendingRead.Membership, scope, ctx, out c);
            else
                c = EvalPointer(cnf.info.collectionPointer, scope, ctx);
            object? target = EvalPointer(cnf.info.valuePointer, scope, ctx);
            if (pending is not null && cnf.info.reference)
            {
                // Pending entries are value ids, so identity needs none read.
                ctx.client.NotePendingRead(pending.Row.id);
                return Box(target is not null
                    && ReferenceKeyOf(target, ctx) is string referenceId
                    && ((NeoWriteBatch.PendingList)pending).Contains(referenceId));
            }
            if (pending is not null)
            {
                // An entry with the target's id matches without being read;
                // only a miss compares entries, which reads them all.
                if ((target as string ?? ValueIdOf(target, ctx)) is { } sought
                    && ((NeoWriteBatch.PendingList)pending).Contains(sought))
                {
                    ctx.client.NotePendingRead(pending.Row.id);
                    return BoxedTrue;
                }
                c = ResolveValueIfId(pending.Row.id, ctx, pending.Ownership, pending.CollectionMember);
            }
            if (c is string s)
            {
                if (target is not string ts)
                {
                    throw new NSGetterRuntimeError(
                        "string.Contains argument must be a string");
                }
                return Box(s.Contains(ts));
            }
            if (cnf.info.reference)
                return Box(ReferenceIndexOf(c, target, ctx) >= 0);
            string? targetReferenceId = target as string
                ?? ValueIdOf(target, ctx);
            if (c is object?[] entries)
            {
                // An entry whose stored id is the target's matches without
                // being read, whatever the entries before it hold, so a list
                // only builds a cursor to compare entries when no id matches.
                if (entries.Length == 0)
                    return BoxedFalse;
                if (targetReferenceId is not null)
                {
                    for (int i = 0; i < entries.Length; i++)
                    {
                        if (entries[i] is string valueId && valueId == targetReferenceId)
                            return BoxedTrue;
                    }
                }
                var listCursor = new CollectionCursor(c, ctx);
                while (listCursor.MoveNextUnresolved())
                {
                    if (JsEqual(listCursor.ResolveEntry(ctx), target))
                        return BoxedTrue;
                }
                return BoxedFalse;
            }
            var cursor = new CollectionCursor(c, ctx);
            while (cursor.MoveNextUnresolved())
            {
                if ((cursor.ValueId is { } valueId && valueId == targetReferenceId)
                    || JsEqual(cursor.ResolveEntry(ctx), target))
                {
                    return BoxedTrue;
                }
            }
            return BoxedFalse;
        }

        private static object? EvalIndexOf(
            IndexOfFunction iof,
            NeoScriptScope scope,
            Context ctx)
        {
            var c = EvalPointer(iof.info.collectionPointer, scope, ctx);
            if (!CollectionIsList(c))
            {
                throw new NSGetterRuntimeError(
                    "IndexOf receiver must be a List value.");
            }
            var target = EvalPointer(iof.info.valuePointer, scope, ctx);
            if (iof.info.reference)
                return Box(ReferenceIndexOf(c, target, ctx));
            string? targetReferenceId = target as string
                ?? ValueIdOf(target, ctx);
            var cursor = new CollectionCursor(c, ctx);
            while (cursor.MoveNextUnresolved())
            {
                if ((cursor.ValueId is { } valueId && valueId == targetReferenceId)
                    || JsEqual(cursor.ResolveEntry(ctx), target))
                {
                    return Box(cursor.Index);
                }
            }
            return Box(-1);
        }

        private static object? EvalWhere(
            WhereFunction wf,
            NeoScriptScope scope,
            Context ctx)
        {
            var c = EvalPointer(wf.info.collectionPointer, scope, ctx);
            var inner = wf.info.function;
            bool isList = CollectionIsList(c);
            int capacity = CollectionEntryCount(c);
            // A grid query's own result has no other holder: its matches
            // compact into it, behind the entry being read.
            bool inPlace = wf.info.collectionPointer is CallFunctionPointer call
                && NeoScriptGridQueries.ReturnsObjects(call.memberId)
                && TemporaryLists.TakeExclusive(c);
            object?[]? matches = isList
                ? inPlace ? (object?[])c! : new object?[capacity]
                : null;
            int matchCount = 0;
            Dictionary<string, object?>? matchedEntries = isList
                ? null
                : new Dictionary<string, object?>(capacity);
            var callback = new PreparedCollectionCallback(
                inner,
                scope,
                ctx,
                isList,
                CollectionCallbackReturnContract.Predicate);
            try
            {
                var cursor = new CollectionCursor(c, ctx);
                while (cursor.MoveNextUnresolved())
                {
                    object? entry = cursor.ResolveEntry(ctx);
                    if (callback.Test(in cursor, entry))
                    {
                        // Re-emit valueId references rather than dereferenced
                        // entries when we have them — matches TS semantic.
                        object? emit = cursor.ValueId ?? entry;
                        if (matches is not null)
                            AppendResult(ref matches, ref matchCount, emit);
                        else
                            matchedEntries![cursor.Key.ToString()!] = emit;
                    }
                }
                object result;
                if (matches is not null)
                {
                    object?[] list = TrimResult(matches, matchCount);
                    if (inPlace && !ReferenceEquals(list, c))
                        TemporaryLists.Return((object?[])c!);
                    ctx.NoteFreshList(list);
                    result = list;
                }
                else
                {
                    result = matchedEntries!;
                }
                // The source compacted in place already carries its entry member.
                if (!ReferenceEquals(result, c))
                    KeepEntryMember(result, CollectionEntryMember(c, ctx));
                callback.CompleteOperator(result);
                return result;
            }
            finally
            {
                callback.Dispose();
            }
        }

        /// <summary>
        /// Operator results fill an array sized to the source collection: a
        /// fresh array per result, since entry metadata is keyed by it.
        /// </summary>
        private static void AppendResult(ref object?[] results, ref int count, object? value)
        {
            if (count == results.Length)
                Array.Resize(ref results, Math.Max(4, count * 2));
            results[count++] = value;
        }

        private static object?[] TrimResult(object?[] results, int count)
        {
            if (count != results.Length)
                Array.Resize(ref results, count);
            return results;
        }

        private static object? EvalFirst(
            Function fn,
            NeoScriptScope scope,
            Context ctx)
        {
            // Both share the optional-predicate shape. Switch on the
            // function class to choose throw-vs-null on no-match.
            bool isFirst = fn is FirstFunction;
            FunctionCollectionOptionalBoolInfo info = isFirst
                ? ((FirstFunction)fn).info
                : ((FirstOrDefaultFunction)fn).info;
            var c = EvalPointer(info.collectionPointer, scope, ctx);
            var inner = info.function;
            bool isList = CollectionIsList(c);
            if (inner is null)
            {
                var cursor = new CollectionCursor(c, ctx);
                if (cursor.MoveNextUnresolved())
                    return cursor.ResolveEntry(ctx);
            }
            else
            {
                var callback = new PreparedCollectionCallback(
                    inner,
                    scope,
                    ctx,
                    isList,
                    CollectionCallbackReturnContract.Predicate);
                try
                {
                    var cursor = new CollectionCursor(c, ctx);
                    while (cursor.MoveNextUnresolved())
                    {
                        object? foundValue = cursor.ResolveEntry(ctx);
                        if (callback.Test(in cursor, foundValue))
                        {
                            callback.CompleteOperator(foundValue);
                            return foundValue;
                        }
                    }
                }
                finally
                {
                    callback.Dispose();
                }
            }
            if (isFirst)
            {
                throw new NSGetterRuntimeError(
                    inner is null
                        ? "First() called on an empty collection"
                        : "First() found no matching entry");
            }
            return null;
        }

        private static object? EvalSelect(
            SelectFunction sf,
            NeoScriptScope scope,
            Context ctx)
        {
            var c = EvalPointer(sf.info.collectionPointer, scope, ctx);
            var inner = sf.info.function;
            bool isList = CollectionIsList(c);
            var results = new object?[CollectionEntryCount(c)];
            int count = 0;
            var callback = new PreparedCollectionCallback(
                inner,
                scope,
                ctx,
                isList,
                CollectionCallbackReturnContract.Projection);
            try
            {
                var cursor = new CollectionCursor(c, ctx);
                while (cursor.MoveNextUnresolved())
                {
                    object? projected = callback.Project(in cursor, cursor.ResolveEntry(ctx));
                    if (!ReferenceEquals(projected, PreparedCollectionCallback.NoProjection))
                        AppendResult(ref results, ref count, projected);
                }
                object?[] result = TrimResult(results, count);
                callback.CompleteOperator(result);
                return result;
            }
            finally
            {
                callback.Dispose();
            }
        }

        /// <summary>
        /// Retains immutable executor configuration and one entry scope for a
        /// collection operator. Locals and expression replay state are reset
        /// independently before each callback entry.
        /// </summary>
        private enum CollectionCallbackReturnContract
        {
            Predicate,
            Projection,
        }

        /// <summary>
        /// A struct, so an operator call allocates nothing for its callback:
        /// the operator owns it as a local and disposes it in a
        /// <c>finally</c>, since a <c>using</c> local is read-only and
        /// <see cref="CompleteOperator"/> writes to it.
        /// </summary>
        private struct PreparedCollectionCallback : IDisposable
        {
            private readonly NeoScriptScope scope;
            private readonly bool isList;
            private readonly int parameterCount;
            private readonly Context ctx;
            private readonly bool returnsConstructedVector;
            // A body that only returns its entry's type test, like the filter
            // GetObjects<T> lowers to, runs the test without a frame.
            private readonly TypeInfo? entryTypeCheck;
            private readonly FunctionWithReturnType body;
            private readonly NeoScriptExecutionOptions options;
            // A body that only returns an expression, the common lambda
            // shape, evaluates it per entry without a statement frame.
            private readonly Pointer? expression;
            private readonly Context expressionContext;
            // A predicate returning a condition tests it without a box.
            private readonly BooleanExpression? condition;
            private readonly bool enclosingConstructorBody;
            private NeoScriptExecutionResult ownerTerminal;

            internal PreparedCollectionCallback(
                FunctionWithReturnType callback,
                NeoScriptScope parentScope,
                Context ctx,
                bool isList,
                CollectionCallbackReturnContract returnContract)
            {
                Variable[] parameters = callback.parameters
                    ?? throw new NSGetterRuntimeError(
                        "Collection callback is missing its parameter metadata.");
                if (parameters.Length < 1)
                {
                    throw new NSGetterRuntimeError(
                        "Collection callback requires at least one parameter.");
                }
                if (parameters.Length > 2)
                {
                    throw new NSGetterRuntimeError(
                        $"Collection callback supports at most two parameters, but received {parameters.Length}.");
                }
                TypeInfo callbackReturnType = callback.typeInfo
                    ?? throw new NSGetterRuntimeError(
                        "Collection callback is missing its compiled return type.");
                if (returnContract == CollectionCallbackReturnContract.Predicate
                    && callbackReturnType.type != MemberKind.Bool)
                {
                    throw new NSGetterRuntimeError(
                        "Collection predicate callback must declare a Bool return type.");
                }
                if (returnContract == CollectionCallbackReturnContract.Predicate
                    && !callbackReturnType.required)
                {
                    throw new NSGetterRuntimeError(
                        "Collection predicate callback must declare a required return type.");
                }

                // Only the fields a callback uses are stored: each reference
                // store into the struct costs a GC write barrier.
                this = default;
                // A callback cannot suspend, so nothing retains its scope
                // once the operator ends; it is pooled like a function frame.
                NeoScriptScopeLayout layout = callback.scopeLayout ??= new NeoScriptScopeLayout(callback);
                // Validates the body, so a rejected callback has rented nothing.
                NeoScriptExecutor.EnterCallback(callback, ctx);
                this.ctx = ctx;
                parameterCount = parameters.Length;
                if (ctx.collectionCallbackPreparationMetrics is not null)
                {
                    ctx.collectionCallbackPreparationMetrics
                        .BindingPlanCreations++;
                    ctx.collectionCallbackPreparationMetrics.BodyValidations++;
                }
                Instruction[]? statements = callback.instructions;
                if (statements is { Length: 1 }
                    && statements[0] is ReturnInstruction { pointer: IsCheckPointer { pointer: VariablePointer tested } check }
                    && tested.variableId == parameters[parameterCount - 1].id)
                {
                    // The type test runs on the entry alone, in no scope.
                    entryTypeCheck = check.checkType;
                    return;
                }
                // A body that only returns a vector it constructs hands back
                // a value nothing else holds, which needs no defensive copy.
                returnsConstructedVector = statements is { Length: 1 }
                    && statements[0] is ReturnInstruction { pointer: FunctionPointer { function: VectorConstructorFunction constructor } }
                    && constructor.info.vectorType == callbackReturnType.type;
                scope = layout.RentScope();
                scope.BindParent(parentScope);
                this.isList = isList;
                body = callback;
                options = NeoScriptExecutionOptions.ForImmediate(ctx.client);
                expression = NeoScriptExecutor.CallbackExpression(callback);
                expressionContext = expression is null
                    ? ctx
                    : NeoScriptExecutor.ImmediateExpressionContext(ctx.client, ctx, options);
                condition = returnContract == CollectionCallbackReturnContract.Predicate
                    && expression is OperationPointer { operation: BooleanOperation booleanOperation }
                        ? booleanOperation.expression
                        : null;
                // A callback runs on its caller's context but is not a
                // constructor body's own statement.
                enclosingConstructorBody = ctx.constructorBody;
                ctx.constructorBody = false;
            }

            // Project's answer when the body returned nothing.
            internal static readonly object NoProjection = new();

            // Shared, so a projection copies no subject struct per entry.
            private static readonly NeoScriptValueMarshaller.ValueSubject ProjectionSubject =
                "collection projection callback return value";

            /// <summary>
            /// A projection callback's value for one entry, or
            /// <see cref="NoProjection"/> when its body returned nothing.
            /// </summary>
            internal object? Project(in CollectionCursor cursor, object? entry)
            {
                if (entryTypeCheck is not null)
                    return Box(RuntimeTypeCheck(entry, entryTypeCheck, ctx));
                BindEntry(in cursor, entry);
                TypeInfo returnType = body.typeInfo!;
                object? value;
                if (expression is null)
                {
                    NeoScriptExecutionResult result = Run();
                    if (!result.Returned)
                        return NoProjection;
                    value = result.ReturnValue;
                }
                else
                {
                    value = EvaluateValue(expression, scope, expressionContext, out double number);
                    if (ReferenceEquals(value, ArithmeticValue.BareNumber))
                    {
                        value = Box(number);
                        // A finite number is already a Float, and a whole one an Int.
                        if (returnType.type == MemberKind.Float && double.IsFinite(number)
                            || returnType.type == MemberKind.Int && NeoScriptValueMarshaller.IsIntegralNumber(number))
                        {
                            return value;
                        }
                    }
                    value = NeoScriptExecutor.CoerceReturnValue(value, returnType);
                }
                if (returnsConstructedVector)
                    return value;
                return NeoScriptValueMarshaller.NormalizeResolved(
                    ctx.client,
                    ctx.valueOwnership,
                    value,
                    returnType,
                    ctx,
                    ProjectionSubject);
            }

            /// <summary>Runs a predicate callback for one entry.</summary>
            internal bool Test(in CollectionCursor cursor, object? entry)
            {
                if (entryTypeCheck is not null)
                    return RuntimeTypeCheck(entry, entryTypeCheck, ctx);
                BindEntry(in cursor, entry);
                if (condition is not null)
                    return EvalBooleanExpression(condition, scope, expressionContext);
                object? returned = expression is null
                    ? Run().ReturnValue
                    : NeoScriptExecutor.CoerceReturnValue(
                        EvaluatePointer(expression, scope, expressionContext),
                        body.typeInfo!);
                if (returned is not bool passed)
                {
                    throw new NSGetterRuntimeError(
                        "Collection predicate callback returned a value that does not match its required Bool contract.");
                }
                return passed;
            }

            // Runs a statement body, which cannot suspend.
            private NeoScriptExecutionResult Run()
            {
                NeoScriptExecutionResult result = NeoScriptExecutor.ExecuteCallback(ctx.client, body, scope, ctx, options);
                if (result.IsPaused)
                {
                    result.Deferred?.DisposeFromOwner(
                        "collection callback cannot suspend");
                    throw new NSGetterRuntimeError(
                        "Collection callbacks cannot call deferred Functions.");
                }
                return result;
            }

            private void BindEntry(in CollectionCursor cursor, object? entry)
            {
                scope.ResetInvocationLocals(parameterCount);
                // Parameter i is layout slot i: (key, entry) or (entry).
                if (parameterCount == 2)
                    scope.SetParameter(0, isList ? Box(cursor.Index) : cursor.Key.ToString());
                scope.SetParameter(parameterCount - 1, entry);
            }

            internal void CompleteOperator(object? returnValue)
            {
                ownerTerminal = NeoScriptExecutionResult.Completed(
                    returned: true, returnValue);
            }

            public void Dispose()
            {
                ctx.allocationTracker.ExitExecution(ctx.client, ctx, in ownerTerminal);
                if (entryTypeCheck is not null)
                    return;
                ctx.constructorBody = enclosingConstructorBody;
                body.scopeLayout!.ReturnScope(scope);
            }
        }

        private static object? EvalListIndex(
            FunctionListIndexInfo info,
            NeoScriptScope scope,
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
            if (FindRowReference(collection, ctx) is not { } row)
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
            // An Asset List is immutable for the life of a candidate replay,
            // so its wrapper (and derived index) is resolved through the
            // committed registry rather than rebuilt inside every candidate.
            bool committedAssetList = row.ownership == NeoValueOwnership.Asset;
            bool found = committedAssetList
                ? ctx.client.TryGetCommittedNode(
                    info.listMemberId,
                    row.valueId,
                    row.ownership,
                    out NeoMember? existing)
                : ctx.client.TryGetNode(
                    info.listMemberId,
                    row.valueId,
                    row.ownership,
                    out existing);
            if (found && existing is NeoMemberList existingList)
            {
                listNode = existingList;
            }
            else
            {
                NeoMember created = committedAssetList
                    ? ctx.client.CreateCommittedNode(
                        () => NeoMember.Create(ctx.client, listMember, row.valueId))
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
                    if (!index.TryGetUnique(rawKey, out string? valueId))
                        return null;
                    return ResolveValueIfId(valueId, ctx, row.ownership);
                }
                IReadOnlyList<string> valueIds = index.GetMany(rawKey);
                var result = new object?[valueIds.Count];
                for (int i = 0; i < valueIds.Count; i++)
                    result[i] = valueIds[i];
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
            if (c is object?[] arr)
                return arr.Length;
            if (c is IDictionary<string, object?> dict)
                return dict.Count;
            if (c is string s)
                return s.Length;
            throw new NSGetterRuntimeError(
                $"Cannot Count() {ReceiverTypeName(c)}; expected list, dictionary, or string");
        }

        private static int CollectionEntryCount(object? collection)
        {
            if (collection is object?[] list)
                return list.Length;
            if (collection is IDictionary<string, object?> dictionary)
                return dictionary.Count;
            return 0;
        }

        private static object EvalVectorConstructor(
            FunctionVectorConstructorInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            Pointer[] pointers = info.componentPointers;
            int arity = pointers.Length;
            // Vectors have at most three components; they stay in locals.
            float x = 0;
            float y = 0;
            float z = 0;
            for (int i = 0; i < arity; i++)
            {
                float component = VectorComponent(info.vectorType, pointers[i], scope, ctx);
                if (i == 0)
                    x = component;
                else if (i == 1)
                    y = component;
                else if (i == 2)
                    z = component;
            }

            switch (info.vectorType)
            {
                case MemberKind.Vector2:
                    EnsureVectorArity(arity, 2, info.vectorType);
                    return new NeoVector2Value { x = x, y = y };
                case MemberKind.Vector2Int:
                    EnsureVectorArity(arity, 2, info.vectorType);
                    RequireIntegerComponent(x, "x");
                    RequireIntegerComponent(y, "y");
                    return new NeoVector2Value { x = x, y = y };
                case MemberKind.Vector3:
                    EnsureVectorArity(arity, 3, info.vectorType);
                    return new NeoVector3Value { x = x, y = y, z = z };
                case MemberKind.Vector3Int:
                    EnsureVectorArity(arity, 3, info.vectorType);
                    RequireIntegerComponent(x, "x");
                    RequireIntegerComponent(y, "y");
                    RequireIntegerComponent(z, "z");
                    return new NeoVector3Value { x = x, y = y, z = z };
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
            NeoScriptScope scope,
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
            if (!NeoNumbers.IsWhole(sliceIndex))
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
            NeoScriptScope scope,
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
                        return (double)(float)NeoDecimalMath.ToFloat(
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
            NeoScriptScope scope,
            Context ctx)
        {
            if (info.digitsPointer is null)
            {
                throw new NSGetterRuntimeError(
                    $"Decimal {info.op} is missing its digits pointer.");
            }
            var value = EvalPointer(info.digitsPointer, scope, ctx);
            if (!TryAsDouble(value, out double number) || !NeoNumbers.IsWhole(number))
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
            NeoScriptScope scope,
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
                        return result;
                    }
                case StringOpKind.ToUpper:
                    {
                        string result = receiverText.ToUpperInvariant();
                        return result;
                    }
                case StringOpKind.Trim:
                    {
                        string result = receiverText.Trim();
                        return result;
                    }
                case StringOpKind.Replace:
                    {
                        if (info.argPointer is null)
                            throw new NSGetterRuntimeError("string.Replace requires a search argument.");
                        if (info.replacementPointer is null)
                            throw new NSGetterRuntimeError("string.Replace requires a replacement argument.");
                        var search = EvalPointer(info.argPointer, scope, ctx);
                        var replacement = EvalPointer(info.replacementPointer, scope, ctx);
                        if (search is not string searchText)
                            throw new NSGetterRuntimeError("string.Replace search must be a string.");
                        if (replacement is not string replacementText)
                            throw new NSGetterRuntimeError("string.Replace replacement must be a string.");
                        if (searchText.Length == 0)
                            throw new NSGetterRuntimeError("string.Replace search must not be empty.");
                        return receiverText.Replace(searchText, replacementText);
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

        // ---------------------------------------------------------------
        // Math builtins (P69 §5.2). `Math` is a compiler intrinsic, not a
        // class: arguments arrive as plain pointers and results are ordinary
        // numbers — boxed doubles on the float path, canonical decimal
        // strings on the decimal path, exactly like arithmetic operations.
        // ---------------------------------------------------------------

        /// <summary>
        /// P69 §5.3 — the clamp bounds error, shared by the float and the
        /// decimal arm. Like every Math error it names no value: the parity
        /// harness compares these strings byte-for-byte and number
        /// stringification differs between hosts at the extremes.
        /// </summary>
        private const string MathClampBoundsMessage = "Math.Clamp requires min <= max.";

        /// <summary>
        /// Evaluates a <c>mathOp</c> builtin — the <c>Math</c> namespace's
        /// numeric functions — mirroring the TS evaluator's
        /// <c>NSFunctionType.mathOp</c> case. Float arms delegate straight to
        /// <see cref="System.Math"/>; <c>decimal</c>-stamped arms run the
        /// exact decimal core (<see cref="NeoDecimalMath"/>), the same split
        /// arithmetic operations make. Every failure is a plain
        /// <see cref="NSGetterRuntimeError"/>, so authored <c>try</c> can
        /// catch it like division by zero.
        /// </summary>
        private static object? EvalMathOp(
            FunctionMathOpInfo info,
            NeoScriptScope scope,
            Context ctx,
            out double number)
        {
            MathOp op = MathOf(info);
            int arity = MathOpArity(op);
            if (info.argPointers.Length != arity)
            {
                throw new NSGetterRuntimeError(
                    $"Math.{MathOpFunctionName(info.op)} takes {arity} arguments; got {info.argPointers.Length}.");
            }
            // Math intrinsics take at most three operands. Plain locals, not a
            // span or a capturing helper: either moves them to memory, where
            // every reference store pays a write barrier.
            Pointer[] pointers = info.argPointers;
            object? r0 = EvaluateValue(pointers[0], scope, ctx, out double v0);
            object? r1 = null;
            object? r2 = null;
            double v1 = 0;
            double v2 = 0;
            if (arity > 1)
                r1 = EvaluateValue(pointers[1], scope, ctx, out v1);
            if (arity > 2)
                r2 = EvaluateValue(pointers[2], scope, ctx, out v2);
            // Evaluate all arguments before validating any of them.
            if (r0 is null || (arity > 1 && r1 is null) || (arity > 2 && r2 is null))
                throw new NSGetterRuntimeError($"Math.{MathOpFunctionName(info.op)} argument is null.");
            if (info.isDecimal == true)
            {
                var decimalArgs = new object?[arity];
                decimalArgs[0] = ArithmeticValue.Box(r0, v0);
                if (arity > 1)
                    decimalArgs[1] = ArithmeticValue.Box(r1, v1);
                if (arity > 2)
                    decimalArgs[2] = ArithmeticValue.Box(r2, v2);
                number = 0;
                return EvalDecimalMathOp(op, MathOpFunctionName(info.op), decimalArgs);
            }
            v0 = FloatMathArgument(info.op, r0, v0);
            if (arity > 1)
                v1 = FloatMathArgument(info.op, r1, v1);
            if (arity > 2)
                v2 = FloatMathArgument(info.op, r2, v2);
            number = EvalFloatMathOp(op, info.op, v0, v1, v2);
            return ArithmeticValue.BareNumber;
        }

        private static double FloatMathArgument(string wireOp, object? value, double number)
        {
            if (!ArithmeticValue.IsNumeric(value))
                throw new NSGetterRuntimeError(
                    $"Math.{MathOpFunctionName(wireOp)} argument is not numeric: {ReceiverTypeName(value)}.");
            return ArithmeticValue.NumberOf(value, number);
        }

        /// <summary>
        /// The IEEE double arm. Results are boxed doubles even where the
        /// declared type is Int (<c>Round</c>/<c>Floor</c>/<c>Ceiling</c>/
        /// <c>Truncate</c>/<c>Sign</c>) — NeoScript's Int is an integral
        /// Float at runtime — which is exactly why the four Int-returning
        /// conversions reject a non-finite argument up front instead of
        /// minting a value the runtime's integral validation would refuse
        /// downstream (P69 §2.5).
        /// </summary>
        /// <param name="wireOp">The op as the IR spells it, for error messages.</param>
        /// <param name="a">The first operand.</param>
        /// <param name="b">The second operand of a binary or ternary op.</param>
        /// <param name="c">The third operand of a ternary op.</param>
        private static double EvalFloatMathOp(MathOp op, string wireOp, double a, double b, double c)
        {
            switch (op)
            {
                // Min/Max propagate NaN and order -0.0 below 0.0 in both
                // hosts, so the standard APIs are the contract (P69 §2.2).
                case MathOp.Min:
                    return System.Math.Min(a, b);
                case MathOp.Max:
                    return System.Math.Max(a, b);
                case MathOp.Clamp:
                    {
                        // System.Math.Clamp's algorithm, spelled out because the
                        // web has no native clamp and the guard must raise our
                        // message rather than the host's (P69 §2.3). The guard is
                        // a real comparison, so NaN bounds never trip it, and a
                        // NaN value falls through both tests and is returned.
                        double value = a;
                        double min = b;
                        double max = c;
                        if (min > max)
                        {
                            throw new NSGetterRuntimeError(MathClampBoundsMessage);
                        }
                        if (value < min)
                            return min;
                        if (value > max)
                            return max;
                        return value;
                    }
                case MathOp.Round:
                    RequireFiniteMathArgument(wireOp, a);
                    // System.Math.Round(double)'s default midpoint mode is
                    // already ToEven, and half-even is the pinned
                    // cross-runtime contract (P69 §2.4) — the same midpoint
                    // rule decimal.Round(digits) has always used. The web
                    // evaluator hand-rolls it, because JS rounds half up.
                    return System.Math.Round(a);
                case MathOp.Floor:
                    RequireFiniteMathArgument(wireOp, a);
                    return System.Math.Floor(a);
                case MathOp.Ceiling:
                    RequireFiniteMathArgument(wireOp, a);
                    return System.Math.Ceiling(a);
                case MathOp.Truncate:
                    RequireFiniteMathArgument(wireOp, a);
                    return System.Math.Truncate(a);
                case MathOp.Abs:
                    return System.Math.Abs(a);
                case MathOp.Sign:
                    {
                        // System.Math.Sign's three-way answer, not JS's ±0/NaN
                        // one: NaN is a runtime error and both zeros give 0
                        // (P69 §2.6).
                        double value = a;
                        if (double.IsNaN(value))
                        {
                            throw new NSGetterRuntimeError("Math.Sign is undefined for NaN.");
                        }
                        if (value > 0)
                            return 1d;
                        if (value < 0)
                            return -1d;
                        return 0d;
                    }
                // Correctly rounded by IEEE 754 in both hosts, then to the
                // 32-bit float Sqrt returns. NaN for a negative argument.
                case MathOp.Sqrt:
                    return (double)(float)System.Math.Sqrt(a);
                default:
                    throw new NSGetterRuntimeError($"Unknown math op '{wireOp}'.");
            }
        }

        /// <summary>
        /// The exact-decimal arm: operands are canonical decimal strings (Int
        /// arguments widen exactly through
        /// <see cref="CoerceDecimalOperand"/>), and results stay decimals —
        /// canonical scale-0 for the integer-valued ops, matching
        /// <c>System.Math.Floor(decimal)</c>'s shape — except <c>Sign</c>,
        /// which is Int-typed on every numeric input.
        /// </summary>
        private static object EvalDecimalMathOp(MathOp op, string name, object?[] args)
        {
            var values = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is not string && !TryAsDouble(args[i], out _))
                {
                    throw new NSGetterRuntimeError(
                        $"Math.{name} argument is not numeric: {ReceiverTypeName(args[i])}.");
                }
                // The widening seam's own messages name the operation, and
                // the web evaluator spells that context `Math.<Fn>`; these
                // strings are parity-compared like every other one.
                values[i] = CoerceDecimalOperand(args[i], $"Math.{name}");
            }
            try
            {
                switch (op)
                {
                    // Ties return the first argument, which is observable:
                    // "1.10" and "1.1" are equal but not interchangeable.
                    case MathOp.Min:
                        return NeoDecimalMath.Min(values[0], values[1]);
                    case MathOp.Max:
                        return NeoDecimalMath.Max(values[0], values[1]);
                    case MathOp.Clamp:
                        {
                            // The §2.3 algorithm again, on exact comparisons; a
                            // decimal is never NaN, so nothing falls through.
                            if (NeoDecimalMath.Compare(values[1], values[2]) > 0)
                            {
                                throw new NSGetterRuntimeError(MathClampBoundsMessage);
                            }
                            if (NeoDecimalMath.Compare(values[0], values[1]) < 0)
                                return values[1];
                            if (NeoDecimalMath.Compare(values[0], values[2]) > 0)
                                return values[2];
                            return values[0];
                        }
                    // Round(decimal) is defined as exactly x.Round(0), so the
                    // two spellings run the same code and can never drift.
                    case MathOp.Round:
                        return NeoDecimalMath.Round(values[0], 0);
                    case MathOp.Floor:
                        return NeoDecimalMath.Floor(values[0]);
                    case MathOp.Ceiling:
                        return NeoDecimalMath.Ceiling(values[0]);
                    case MathOp.Truncate:
                        return NeoDecimalMath.Truncate(values[0]);
                    case MathOp.Abs:
                        return NeoDecimalMath.Abs(values[0]);
                    case MathOp.Sign:
                        return (double)NeoDecimalMath.Compare(values[0], "0");
                    default:
                        // Sqrt is the one op the compiler refuses on decimals:
                        // no exact decimal square root exists (P69 §2.6).
                        throw new NSGetterRuntimeError(
                            $"Math.{name} does not accept decimal arguments.");
                }
            }
            catch (DecimalOverflowException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
        }

        /// <summary>
        /// Guards the Int-returning ops: a non-finite Float would mint an
        /// Int-typed NaN or Infinity, which the runtime's own integral
        /// validation refuses downstream (P69 §2.5). Min/Max/Clamp/Abs/Sqrt
        /// stay Float-typed and keep host non-finite semantics.
        /// </summary>
        private static void RequireFiniteMathArgument(string wireOp, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new NSGetterRuntimeError(
                    $"Math.{MathOpFunctionName(wireOp)} requires a finite argument.");
            }
        }

        /// <summary>
        /// C#-cased name of a <see cref="MathOpKind"/> as it appears in the
        /// runtime error contract (<c>Math.Ceiling …</c>). An unrecognized op
        /// falls back to its wire spelling; the dispatch switch is what
        /// rejects it.
        /// </summary>
        private static string MathOpFunctionName(string op)
        {
            switch (op)
            {
                case MathOpKind.Min:
                    return "Min";
                case MathOpKind.Max:
                    return "Max";
                case MathOpKind.Clamp:
                    return "Clamp";
                case MathOpKind.Round:
                    return "Round";
                case MathOpKind.Floor:
                    return "Floor";
                case MathOpKind.Ceiling:
                    return "Ceiling";
                case MathOpKind.Truncate:
                    return "Truncate";
                case MathOpKind.Abs:
                    return "Abs";
                case MathOpKind.Sign:
                    return "Sign";
                case MathOpKind.Sqrt:
                    return "Sqrt";
                default:
                    return op;
            }
        }

        /// <summary>
        /// Declared argument count of a <see cref="MathOpKind"/> — defensive,
        /// since the compiler already enforces arity; an unrecognized op is
        /// treated as unary so the dispatch switch reports it.
        /// </summary>
        private static int MathOpArity(MathOp op)
        {
            switch (op)
            {
                case MathOp.Clamp:
                    return 3;
                case MathOp.Min:
                case MathOp.Max:
                    return 2;
                default:
                    return 1;
            }
        }

        // The op parsed once per call site: a string switch hashes and
        // compares it on every evaluation.
        private static MathOp MathOf(FunctionMathOpInfo info)
        {
            if (info.parsedOp != MathOp.Unresolved)
                return info.parsedOp;
            return info.parsedOp = info.op switch
            {
                MathOpKind.Min => MathOp.Min,
                MathOpKind.Max => MathOp.Max,
                MathOpKind.Clamp => MathOp.Clamp,
                MathOpKind.Round => MathOp.Round,
                MathOpKind.Floor => MathOp.Floor,
                MathOpKind.Ceiling => MathOp.Ceiling,
                MathOpKind.Truncate => MathOp.Truncate,
                MathOpKind.Abs => MathOp.Abs,
                MathOpKind.Sign => MathOp.Sign,
                MathOpKind.Sqrt => MathOp.Sqrt,
                _ => MathOp.Unknown,
            };
        }

        // ---------------------------------------------------------------
        // List statics (P71 §5.2). `List` is a builtin type, not a class:
        // `List.Repeat` arrives as a compiler intrinsic with two named
        // argument pointers and produces the same untyped entry array a list
        // literal does.
        // ---------------------------------------------------------------

        /// <summary>
        /// Evaluates a <c>listRepeat</c> builtin — <c>List.Repeat(value,
        /// count)</c> — mirroring the TS evaluator's
        /// <c>NSFunctionType.listRepeat</c> case. The value is evaluated
        /// exactly once and the count exactly once, in that order, so side
        /// effects in either operand happen once regardless of how many
        /// entries come out. Every entry is that single evaluated value: for
        /// reference-typed entries this is the same reference repeated,
        /// precisely <c>Enumerable.Repeat</c>'s semantics (P71 §3).
        ///
        /// <para><c>info.entryTypeInfo</c> is the compile-time join result and
        /// is deliberately not consulted here — this runtime stores entries
        /// untyped, exactly as the <see cref="ListLiteralPointer"/> arm does
        /// with its own <c>typeInfo</c>; the field exists for hosts that
        /// materialize typed collections.</para>
        /// </summary>
        private static object EvalListRepeat(
            FunctionListRepeatInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            object? value = EvalPointer(info.valuePointer, scope, ctx);
            double count = IntegerArgument(
                EvalPointer(info.countPointer, scope, ctx),
                "List.Repeat count");
            if (count < 0)
            {
                throw new NSGetterRuntimeError(
                    $"List.Repeat count must be non-negative; got {FormatIntegralArgument(count)}.");
            }
            if (count > int.MaxValue)
            {
                throw new NSGetterRuntimeError(
                    $"List.Repeat count must be at most {int.MaxValue}; got {FormatIntegralArgument(count)}.");
            }
            var entries = new object?[(int)count];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = value;
            }
            return entries;
        }

        /// <summary>
        /// The TS evaluator's <c>numberArg</c>: an argument that must be an
        /// integer, rejected with the same wording on both runtimes. Booleans,
        /// strings, and decimals (canonical strings here) are non-numeric and
        /// fail the same way a fractional double does.
        /// </summary>
        private static double IntegerArgument(object? value, string name)
        {
            if (TryAsDouble(value, out double number)
                && !double.IsNaN(number)
                && !double.IsInfinity(number)
                && NeoNumbers.IsWhole(number))
            {
                return number;
            }
            throw new NSGetterRuntimeError($"{name} must be an integer");
        }

        /// <summary>
        /// Renders an already-integral argument the way the TS evaluator's
        /// template literal does, so the error text is byte-identical:
        /// invariant digits with no decimal point. Magnitudes past
        /// <see cref="long"/> keep round-trip formatting rather than
        /// overflowing the cast.
        /// </summary>
        private static string FormatIntegralArgument(double value)
        {
            return value >= long.MinValue && value <= long.MaxValue
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        // A computed component arrives unboxed, like an arithmetic operand.
        private static float VectorComponent(
            MemberKind vectorType,
            Pointer pointer,
            NeoScriptScope scope,
            Context ctx)
        {
            object? raw = EvaluateValue(pointer, scope, ctx, out double numeric);
            if ((!ReferenceEquals(raw, ArithmeticValue.BareNumber) && !TryAsDouble(raw, out numeric))
                || double.IsNaN(numeric)
                || double.IsInfinity(numeric))
            {
                throw new NSGetterRuntimeError(
                    $"{vectorType} component must be numeric; got {ReceiverTypeName(ArithmeticValue.Box(raw, numeric))}.");
            }
            return (float)numeric;
        }

        private static void EnsureVectorArity(
            int arity,
            int expected,
            MemberKind vectorType)
        {
            if (arity != expected)
            {
                throw new NSGetterRuntimeError(
                    $"{vectorType} takes {expected} numeric arguments, got {arity}.");
            }
        }

        private static void RequireIntegerComponent(float value, string component)
        {
            if (!NeoNumbers.IsWhole(value))
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

            internal object? Raw
            {
                get;
            }
            internal object Key
            {
                get;
            }
        }

        /// <summary>
        /// The ordered raw memberships retained at <c>foreach</c> entry. A
        /// removed Save/Session child row is retained by reference so the
        /// original entry can still be resolved later in the invocation;
        /// this is a membership snapshot, never a deep value clone. A reused
        /// snapshot keeps its buffers.
        /// </summary>
        internal sealed class CollectionSnapshot
        {
            // The raw entries, copied in one block. Only a row id entry has a
            // retained row and ownership, at its index of the parallel arrays.
            private object?[] raws = Array.Empty<object?>();
            private MemberValue?[]? rows;
            // Each row's value node, so a resolve looks no id up.
            private NeoValueNode?[]? nodes;
            private NeoValueOwnership[]? ownerships;
            private JsonMember? entryMember;

            /// <summary>The entries in use; -1 when no snapshot is taken.</summary>
            internal int Count { get; private set; } = -1;

            internal void Take(object? collection, Context ctx)
            {
                if (collection is not object?[]
                    && collection is not IDictionary<string, object?>)
                {
                    throw new NSGetterRuntimeError(
                        "foreach receiver must be a List, Dictionary, Set/Lookup, or derived collection view.");
                }

                RowReference? collectionRef = FindRowReference(collection, ctx);
                JsonMember? collectionMember = collectionRef?.CollectionMember(ctx.client);
                entryMember = CollectionEntryMember(collectionRef, collection, ctx);
                NeoValueOwnership? collectionOwnership = collectionMember is LookupMember
                    ? null
                    : FindRowOwnershipByReference(collection, ctx);
                int count = 0;
                if (collection is object?[] array)
                {
                    if (raws.Length < array.Length)
                        raws = new object?[array.Length];
                    Array.Copy(array, raws, array.Length);
                    count = array.Length;
                }
                else if (!TryTakeInsertionOrder((IDictionary<string, object?>)collection, out count))
                {
                    foreach (OrderedRawCollectionEntry entry in
                        OrderedRawCollectionEntries(collection))
                    {
                        if (count == raws.Length)
                            Array.Resize(ref raws, Math.Max(4, count * 2));
                        raws[count++] = entry.Raw;
                    }
                }
                for (int index = 0; index < count; index++)
                {
                    if (raws[index] is not string id)
                        continue;
                    if (rows is null || rows.Length < raws.Length)
                    {
                        Array.Resize(ref rows, raws.Length);
                        Array.Resize(ref nodes, raws.Length);
                        Array.Resize(ref ownerships, raws.Length);
                    }
                    // The collection's row keeps each entry's node, as a
                    // cursor's does, so a repeat run looks no id up.
                    NeoValueNode? node = collectionRef?.EntryNode(index, id);
                    NeoValueOwnership ownership = collectionOwnership ?? ResolveOwnershipForValueId(ctx, id, ref node);
                    rows[index] = ctx.client.ReadValue(ownership, id, ref node);
                    nodes![index] = node;
                    collectionRef?.RememberEntryNode(index, count, id, node);
                    ownerships![index] = ownership;
                }
                Count = count;
            }

            // Without an array-index key, a dictionary's ordered entries are
            // its own order, so its values copy without sorting.
            private bool TryTakeInsertionOrder(IDictionary<string, object?> dictionary, out int count)
            {
                count = 0;
                if (raws.Length < dictionary.Count)
                    raws = new object?[dictionary.Count];
                if (dictionary is Dictionary<string, object?> concrete)
                {
                    // Keys and values enumerate in the same order.
                    foreach (string key in concrete.Keys)
                    {
                        if (TryGetEcmaArrayIndex(key, out _))
                            return false;
                    }
                    concrete.Values.CopyTo(raws, 0);
                    count = concrete.Count;
                    return true;
                }
                foreach (var pair in dictionary)
                {
                    if (TryGetEcmaArrayIndex(pair.Key, out _))
                    {
                        Array.Clear(raws, 0, count);
                        count = 0;
                        return false;
                    }
                    raws[count++] = pair.Value;
                }
                return true;
            }

            internal object? Resolve(int index, Context ctx)
            {
                if (raws[index] is not string id)
                {
                    return raws[index];
                }
                NeoValueOwnership resolvedOwnership = ownerships![index];
                ref NeoValueNode? node = ref nodes![index];
                if (ctx.client.ReadOwnRow(resolvedOwnership, id, ref node) is { } currentRow)
                    return UnwrapCached(currentRow, ctx, resolvedOwnership, entryMember, node);
                MemberValue? retainedRow = rows![index];
                return retainedRow is null
                    ? id
                    : UnwrapCached(retainedRow, ctx, resolvedOwnership, entryMember);
            }

            /// <summary>Drops what a finished run held.</summary>
            internal void Clear()
            {
                if (Count > 0)
                {
                    Array.Clear(raws, 0, Count);
                    if (rows is not null)
                    {
                        Array.Clear(rows, 0, Math.Min(Count, rows.Length));
                        Array.Clear(nodes!, 0, Math.Min(Count, nodes!.Length));
                    }
                }
                entryMember = null;
                Count = -1;
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
            // An index is digits only, so most keys fail on their first char.
            if (key.Length == 0
                || key[0] is < '0' or > '9'
                || !uint.TryParse(
                    key,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out index)
                || index == uint.MaxValue)
            {
                index = 0;
                return false;
            }
            // Canonical text only: "01" is a plain key.
            return key.Length == 1 || key[0] != '0';
        }

        private static bool CollectionIsList(object? collection)
        {
            if (collection is object?[])
                return true;
            if (collection is IDictionary<string, object?>)
                return false;
            throw new NSGetterRuntimeError(
                "Collection callback receiver must be a present List or Dictionary value.");
        }

        /// <summary>
        /// Walks a collection's entries in iteration order, resolving each
        /// entry as it is reached. A struct with no callback, so an operator
        /// allocates nothing per entry; <see cref="Key"/> boxes only when read.
        /// </summary>
        private struct CollectionCursor
        {
            private readonly object?[]? array;
            private readonly List<OrderedRawCollectionEntry>? ordered;
            private readonly RowReference? collectionRef;
            private readonly JsonMember? entryMember;
            // The entry member's, resolved once rather than per entry.
            private readonly NeoValueOwnership? entryOwnership;
            private readonly int count;

            internal CollectionCursor(object? collection, Context ctx)
            {
                // Starting from default leaves the usually-null references
                // unstored: a store through the cursor's byref costs a GC
                // write barrier. The context comes back with each read
                // instead of being kept for the same reason.
                this = default;
                RowReference? collectionRef = FindRowReference(collection, ctx);
                if (collectionRef is not null)
                    this.collectionRef = collectionRef;
                if (CollectionEntryMember(collectionRef, collection, ctx) is { } entryMember)
                {
                    this.entryMember = entryMember;
                    entryOwnership = ctx.client.ConcreteDeclaredOwnership(entryMember);
                }
                if (collection is object?[] array)
                {
                    this.array = array;
                    count = array.Length;
                }
                else
                {
                    ordered = new List<OrderedRawCollectionEntry>(OrderedRawCollectionEntries(collection));
                    count = ordered.Count;
                }
                Index = -1;
            }

            internal int Index
            {
                get; private set;
            }
            internal readonly object Key => array is not null ? Index : ordered![Index].Key;
            internal readonly string? ValueId => Raw as string;

            // The current entry's stored value, read again rather than kept:
            // a field store through the cursor's byref costs a write barrier.
            internal readonly object? Raw => array is not null ? array[Index] : ordered![Index].Raw;

            /// <summary>
            /// Advances to the next entry's stored value; <see cref="ResolveEntry"/>
            /// reads the entry itself when the caller needs more than its id.
            /// </summary>
            internal bool MoveNextUnresolved()
            {
                return ++Index < count;
            }

            /// <summary>
            /// The current entry, returned rather than kept: a field store
            /// through the cursor's byref costs a write barrier.
            /// </summary>
            internal readonly object? ResolveEntry(Context ctx)
            {
                object? raw = Raw;
                if (raw is not string id)
                    return raw;
                NeoValueNode? node = collectionRef?.EntryNode(Index, raw);
                object? entry = ResolveValueId(id, ctx, null, entryOwnership, entryMember, ref node);
                collectionRef?.RememberEntryNode(Index, count, raw, node);
                return entry;
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static bool JsTruthy(object? value)
        {
            if (value is null)
                return false;
            if (value is bool b)
                return b;
            if (TryAsDouble(value, out double d))
                return d != 0 && !double.IsNaN(d);
            if (value is string s)
                return s.Length > 0;
            return true;
        }

        private static bool JsEqual(object? a, object? b)
        {
            if (a is null || b is null)
                return a is null && b is null;
            // Option ids, the usual operands, are never numbers.
            if (a is string sa)
                return b is string sb && sa == sb;
            // Numeric-tolerant equality (int vs double both come through as numbers).
            if (TryAsDouble(a, out double da) && TryAsDouble(b, out double db))
                return da == db;
            if (ReferenceEquals(a, b))
                return true;
            if (a is NeoDelegateValue { IsMemberTarget: true } leftDelegate
                && b is NeoDelegateValue { IsMemberTarget: true } rightDelegate)
                return leftDelegate.memberId == rightDelegate.memberId && leftDelegate.valueId == rightDelegate.valueId;
            if (a is bool ba && b is bool bb)
                return ba == bb;
            if (a is object?[] aa && b is object?[] ab)
            {
                if (aa.Length != ab.Length)
                    return false;
                for (int i = 0; i < aa.Length; i++)
                {
                    if (!JsEqual(aa[i], ab[i]))
                        return false;
                }
                return true;
            }
            if (a is IDictionary<string, object?> ad && b is IDictionary<string, object?> bd)
            {
                if (ad.Count != bd.Count)
                    return false;
                // Records are Dictionary instances: enumerate them without
                // boxing an enumerator.
                if (ad is Dictionary<string, object?> records)
                {
                    foreach (var kvp in records)
                    {
                        if (!SameEntry(kvp, bd))
                            return false;
                    }
                    return true;
                }
                foreach (var kvp in ad)
                {
                    if (!SameEntry(kvp, bd))
                        return false;
                }
                return true;
            }
            return Equals(a, b);
        }

        /// <summary>
        /// Identity equality for Class, interface, and collection values: the
        /// same instance, or two reads of one stored value (raw value ids
        /// included).
        /// </summary>
        internal static bool ReferenceEqual(object? a, object? b, Context ctx)
        {
            if (a is null || b is null)
                return a is null && b is null;
            if (ReferenceEquals(a, b))
                return true;
            object? key = ReferenceKeyOf(a, ctx);
            return key is not null && key.Equals(ReferenceKeyOf(b, ctx));
        }

        /// <summary>
        /// What identity compares: a value id, or a detached object itself
        /// until it attaches. Never attaches rows, so comparing a temporary
        /// leaves it detached.
        /// </summary>
        internal static object? ReferenceKeyOf(object value, Context ctx)
        {
            if (value is string id)
                return id;
            if (value is NeoGeneratedClassValue { PendingValue: NeoScriptObject pending })
                return pending.attachedId ?? (object)pending;
            if (value is NeoScriptObject detached)
                return detached.attachedId ?? (object)detached;
            return ValueIdOf(value, ctx);
        }

        /// <summary>
        /// The index of the first entry identical to <paramref name="target"/>,
        /// or -1. Entries compare unresolved: a stored entry is its value id.
        /// </summary>
        private static int ReferenceIndexOf(object? collection, object? target, Context ctx)
        {
            object? targetKey = target is null ? null : ReferenceKeyOf(target, ctx);
            if (collection is object?[] entries)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (IsSameReference(entries[i], target, targetKey, ctx))
                        return i;
                }
                return -1;
            }
            var cursor = new CollectionCursor(collection, ctx);
            while (cursor.MoveNextUnresolved())
            {
                if (IsSameReference(cursor.Raw, target, targetKey, ctx))
                    return cursor.Index;
            }
            return -1;
        }

        private static bool IsSameReference(object? entry, object? target, object? targetKey, Context ctx) =>
            ReferenceEquals(entry, target)
            || (targetKey is not null && entry is not null && targetKey.Equals(ReferenceKeyOf(entry, ctx)));

        private static bool SameEntry(KeyValuePair<string, object?> entry, IDictionary<string, object?> other) =>
            other.TryGetValue(entry.Key, out var value) && JsEqual(entry.Value, value);

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
        private static bool RuntimeTypeCheck(object? value, TypeInfo checkType, Context ctx,
            HashSet<object>? seenCollections = null)
        {
            if (checkType.type == MemberKind.Null)
                return value is null;
            if (value is null)
                return false;
            if (value is NeoScriptObject { attachedId: null }
                && checkType.type is not (MemberKind.Class or MemberKind.Interface or MemberKind.Dictionary))
            {
                return false;
            }
            switch (checkType.type)
            {
                case MemberKind.Bool:
                    return value is bool;
                case MemberKind.Int:
                    return TryAsDouble(value, out double di) && NeoNumbers.IsWhole(di);
                case MemberKind.Float:
                    return TryAsDouble(value, out _);
                case MemberKind.String:
                    return value is string;
                case MemberKind.Sprite:
                    return value is IDictionary<string, object?> sprite &&
                        sprite.TryGetValue("fileId", out var spriteFileId) &&
                        spriteFileId is string &&
                        sprite.TryGetValue("sliceIndex", out var sliceIndex) &&
                        TryAsDouble(sliceIndex, out double slice) &&
                        NeoNumbers.IsWhole(slice);
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
                case MemberKind.List:
                    return value is object?[];
                case MemberKind.Lookup:
                    {
                        if (value is not object?[] entries || checkType is not LookupTypeInfo lookup)
                            return false;
                        if (seenCollections?.Contains(value) == true)
                            return false;
                        var seen = seenCollections is null
                            ? new HashSet<object>(ReferenceEqualityComparer.Instance)
                            : new HashSet<object>(seenCollections, ReferenceEqualityComparer.Instance);
                        seen.Add(value);
                        foreach (object? entry in entries)
                        {
                            TypeInfo entryType = lookup.entryTypeInfo;
                            if (entry is null && !entryType.required)
                                continue;
                            if (entry is string && (entryType.type == MemberKind.Enum
                                || entryType.type == MemberKind.DialogueLookup))
                                continue;
                            if (RuntimeTypeCheck(entry, entryType, ctx, seen))
                                continue;
                            object? resolved = ResolveValueIfId(entry, ctx);
                            if (ReferenceEquals(resolved, entry)
                                || !RuntimeTypeCheck(resolved, entryType, ctx, seen))
                                return false;
                        }
                        return true;
                    }
                case MemberKind.Dictionary:
                    return value is IDictionary<string, object?>;
                case MemberKind.Enum:
                    {
                        if (value is not object?[] arr)
                            return false;
                        foreach (var e in arr)
                            if (e is not string)
                                return false;
                        return true;
                    }
                case MemberKind.Class:
                    {
                        if (value is not IDictionary<string, object?>)
                            return false;
                        return RuntimeClassExtends(value, (checkType as ClassTypeInfo)?.classId ?? "", ctx);
                    }
                case MemberKind.Interface:
                    {
                        if (value is not IDictionary<string, object?>)
                            return false;
                        string? runtimeClassId = FindRowClassIdByReference(value, ctx);
                        if (string.IsNullOrEmpty(runtimeClassId))
                            return false;
                        string interfaceId = (checkType as InterfaceTypeInfo)?.interfaceId ?? "";
                        if (string.IsNullOrEmpty(interfaceId))
                            return false;
                        return NeoInterfaceResolution.ClassImplements(
                            runtimeClassId!,
                            interfaceId,
                            ctx.client.ProjectDataForRuntime);
                    }
                default:
                    return false;
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
        internal static object? ResolveValueIfId(
            object? at,
            Context ctx,
            NeoValueOwnership? preferredOwnership = null,
            JsonMember? member = null)
        {
            NeoValueNode? node = null;
            return ResolveValueIfId(at, ctx, preferredOwnership, member, ref node);
        }

        /// <summary>
        /// <see cref="ResolveValueIfId(object?, Context, NeoValueOwnership?, JsonMember?)"/>
        /// through the id's node when the caller keeps one.
        /// </summary>
        private static object? ResolveValueIfId(
            object? at,
            Context ctx,
            NeoValueOwnership? preferredOwnership,
            JsonMember? member,
            ref NeoValueNode? node)
        {
            if (at is not string id)
                return at;
            NeoValueOwnership? declared = preferredOwnership is null && member is not null
                ? ctx.client.ConcreteDeclaredOwnership(member)
                : null;
            return ResolveValueId(id, ctx, preferredOwnership, declared, member, ref node);
        }

        /// <param name="declaredOwnership">
        /// <paramref name="member"/>'s concrete declared ownership, which
        /// decides when there is no <paramref name="preferredOwnership"/>.
        /// </param>
        private static object? ResolveValueId(
            string id,
            Context ctx,
            NeoValueOwnership? preferredOwnership,
            NeoValueOwnership? declaredOwnership,
            JsonMember? member,
            ref NeoValueNode? node)
        {
            ctx.client.ReadNestedConstructorResult(id);
            NeoValueOwnership ownership = preferredOwnership is NeoValueOwnership parent
                ? ctx.client.ChildOwnership(member, parent)
                : declaredOwnership ?? ResolveOwnershipForValueId(ctx, id, ref node);
            if (ctx.client.ReadReplayReference(id, ref node, ownership) is not { } row)
                return id;
            var v = UnwrapCached(row, ctx, ownership, member, node);
            return member is LookupMember lookup && v is object?[] ids
                ? ReadLookupSelection(ids, lookup, ctx, node)
                : v;
        }

        /// <summary>
        /// A lookup's read value: a single selection's object, otherwise the
        /// selected ids.
        /// </summary>
        /// <param name="lookupNode">The lookup row's node, which keeps its single selection's node.</param>
        private static object? ReadLookupSelection(
            object?[] ids,
            LookupMember lookup,
            Context ctx,
            NeoValueNode? lookupNode = null)
        {
            if (lookup.Selection != NeoMemberSelectionKind.Multi
                && ids.Length == 1
                && ids[0] is string singleId)
            {
                NeoValueNode? node = lookupNode is not null
                    && ReferenceEquals(lookupNode.selectedId, singleId)
                        ? lookupNode.selectedNode
                        : null;
                var singleOwnership = ResolveLookupSelectionOwnership(ctx, lookup, singleId, ref node);
                MemberValue? next = ctx.client.ReadReplayReference(singleId, ref node, singleOwnership);
                if (lookupNode is not null
                    && node is { live: true }
                    && !ReferenceEquals(lookupNode.selectedNode, node))
                {
                    lookupNode.selectedId = singleId;
                    lookupNode.selectedNode = node;
                }
                if (next is not null)
                    return UnwrapCached(next, ctx, singleOwnership, node: node);
            }
            return ids;
        }

        private static NeoValueOwnership ResolveLookupSelectionOwnership(
            Context ctx, LookupMember lookup, string selectedId)
        {
            NeoValueNode? node = null;
            return ResolveLookupSelectionOwnership(ctx, lookup, selectedId, ref node);
        }

        private static NeoValueOwnership ResolveLookupSelectionOwnership(
            Context ctx, LookupMember lookup, string selectedId, ref NeoValueNode? node)
        {
            // A sparse selected object can still be an authored row while its
            // fields are overridden in the lookup collection's Save/Session store.
            if (ctx.client.TryGetLookupCollectionOwnership(lookup, out NeoValueOwnership ownership))
                return ownership;
            return ResolveOwnershipForValueId(ctx, selectedId, ref node);
        }

        private static object? UnwrapGeneratedValue(object? value, Context ctx)
        {
            // A pending C# view hands back its temporary; asking it for an
            // id would make rows nothing here needs.
            if (value is NeoGeneratedClassValue { PendingValue: { } pending })
                value = pending;
            if (value is NeoScriptObject detached)
                return detached.attachedId is null ? detached : ForwardDetached(detached, ctx);
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                string id = reference.valueId!;
                RowReference? rowRef = FindRowReference(value, ctx);
                var ownership = value is NeoGeneratedClassValue generated
                    ? generated.ValueOwnership
                    : value is NeoObjectRecord record
                        ? record.valueOwnership
                        : rowRef?.ownership ?? ResolveOwnershipForValueId(ctx, id);
                bool ownRow = rowRef is not null && SameId(rowRef.valueId, id);
                NeoValueNode? node = ownRow ? rowRef!.node : null;
                MemberValue? row = ctx.client.ReadReplayReference(id, ref node, ownership);
                bool found = row is not null;
                // A repeat read keeps its node; rewriting it pays a write barrier.
                if (ownRow && !ReferenceEquals(rowRef!.node, node))
                    rowRef.node = node;
                // Older generated callers may wrap a row id without carrying
                // its store. Retain that fallback only when the supplied view
                // cannot resolve the row; a valid sparse view keeps its ownership.
                if (!found && value is NeoGeneratedClassValue)
                {
                    ownership = ResolveOwnershipForValueId(ctx, id);
                    ctx.client.TryGetReplayReference(id, out row, ownership);
                }
                if (row is not null)
                {
                    return UnwrapCached(row, ctx, ownership, rowRef?.member, found ? node : null);
                }
            }
            return value;
        }

        private static NeoValueOwnership ResolveOwnershipForValueId(
            Context ctx,
            string valueId)
        {
            NeoValueNode? node = null;
            return ResolveOwnershipForValueId(ctx, valueId, ref node);
        }

        private static NeoValueOwnership ResolveOwnershipForValueId(
            Context ctx,
            string valueId,
            ref NeoValueNode? node)
        {
            return ctx.client.TryGetValueOwnership(valueId, ref node, out NeoValueOwnership ownership)
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
        /// <see cref="ConstructorReferenceOf"/> bound to <paramref name="ctx"/>.
        /// Its own method so the closure is allocated only where the
        /// delegate is built, not on every construction's entry.
        /// </summary>
        private static Func<object?, NeoConstructorValueReference?> ConstructorReferences(Context ctx) =>
            value => ConstructorReferenceOf(value, ctx);

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
            if (string.IsNullOrEmpty(valueId))
                return null;
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
                    : member is ListMember list && ctx.client.IsUnorderedList(list)
                        ? ToObjectArray(NeoMemberList.ResolveEntryValueIds(ctx.client, a, true))
                        : ToObjectArray(a.value),
                ObjectMemberValue o => o.value is null
                    ? null
                    : ToObjectDict(row.id, ownership, o.value),
                DelegateMemberValue d => d.value,
                ActionMemberValue a => a.value,
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
                // P67 6. A Variant member read hands the runtime the SAME
                // shape a `<Class>.Variants.X` static path does, so the two
                // variant intrinsics need no conversion and no second accepted
                // CLR type. Without this arm the row fell to `_ => null`, so
                // `item.Variant.Initialize()` threw on device while the web
                // evaluator returned the pair, and `item.Variant == null` was
                // unconditionally true on device and false in the editor.
                // P67 6. A Variant member read hands the runtime the SAME
                // shape a `<Class>.Variants.X` static path does, so the two
                // variant intrinsics need no conversion and no second accepted
                // CLR type. Without this arm the row fell to `_ => null`, so
                // `item.Variant.Initialize()` threw on device while the web
                // evaluator returned the pair, and `item.Variant == null` was
                // unconditionally true on device and false in the editor.
                VariantMemberValue vr => vr.value is null
                    ? null
                    : new NeoVariantReference(
                        vr.value.classId,
                        vr.value.variantId,
                        vr.value.rowValueId),
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
            JsonMember? member = null,
            NeoValueNode? node = null)
        {
            if (node is not null && ReferenceEquals(node.id, row.id))
                ctx.client.NoteRowRead(row.id, ref node.capturedIn);
            else
                ctx.client.NoteRowRead(row.id);
            // Scalars have value semantics and no writable CLR aliases. Read the
            // current row directly instead of allocating cache keys and an index
            // entry just to retain a box. Structured values still need identity.
            switch (row)
            {
                case NumberMemberValue number:
                    return number.BoxedValue;
                case BoolMemberValue boolean:
                    return Box(boolean.value);
                case NullMemberValue:
                    return null;
                case StringMemberValue text when member is not StringMember stringMember
                    || stringMember.Format == NeoStringFormatKind.Plain
                    || text.neoLocalizationMode == NeoStringLocalizationMode.Literal:
                    return text.value;
            }
            string? memberId = member?.id;
            if (UnwrapMemo.Find(node, row, ctx, ownership, memberId) is { } remembered)
                return remembered.Value;
            return UnwrapUncached(row, ctx, ownership, member, memberId, node);
        }

        // Its own method so a memo hit's frame stays small.
        private static object? UnwrapUncached(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership,
            JsonMember? member,
            string? memberId,
            NeoValueNode? node)
        {
            RowCacheKey cacheKey = MakeRowCacheKey(ownership, row.id, member);
            if (ctx.rowUnwrapCache.TryGetValue(cacheKey, out var cached))
            {
                UnwrapMemo.Remember(node, row, ctx, ownership, memberId, cached);
                return cached;
            }
            if (member is null && row is ArrayMemberValue)
                ctx.client.TryInferMemberForValueId(row.id, out member);
            var unwrapped = ExtractWireValue(row, ownership, member, ctx);
            ctx.rowUnwrapCache[cacheKey] = unwrapped;
            UnwrapMemo.Remember(node, row, ctx, ownership, memberId, unwrapped);
            RowKey rowCacheKey = RowCacheRowKey(ownership, row.id);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    rowCacheKey, out HashSet<RowCacheKey>? rowKeys))
            {
                rowKeys = new HashSet<RowCacheKey>();
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
                var reference = new RowReference(
                    row.id,
                    ownership,
                    effectiveClassId,
                    member);
                // Records and arrays are built per unwrap; a vector or
                // colour is the row's own instance and may already be indexed.
                if (unwrapped is NeoVector2Value or NeoColorValue)
                    SetRowReference(ctx, unwrapped, reference);
                else
                    AddFreshRowReference(ctx, unwrapped!, reference);
            }
            return unwrapped;
        }

        /// <summary>
        /// A row's canonical unwraps, kept on its value node so a reader
        /// holding the node skips the <see cref="Context.rowUnwrapCache"/>
        /// key. Each entry mirrors one cache entry and is forgotten wherever
        /// that entry is removed or moved.
        /// </summary>
        internal sealed class UnwrapMemo
        {
            private const int MaxEntries = 4;

            private readonly object cache;
            private readonly MemberValue row;
            private readonly NeoValueOwnership ownership;
            private readonly string? memberId;
            private readonly object? value;
            private readonly UnwrapMemo? next;
            private readonly int count;

            private UnwrapMemo(
                object cache,
                MemberValue row,
                NeoValueOwnership ownership,
                string? memberId,
                object? value,
                UnwrapMemo? next)
            {
                this.cache = cache;
                this.row = row;
                this.ownership = ownership;
                this.memberId = memberId;
                this.value = value;
                this.next = next;
                count = (next?.count ?? 0) + 1;
            }

            internal object? Value => value;

            /// <summary>
            /// The entry remembering this unwrap, or null. The entry comes
            /// back rather than its value through an out parameter, which
            /// would cost a GC write barrier on every read.
            /// </summary>
            internal static UnwrapMemo? Find(
                NeoValueNode? node,
                MemberValue row,
                Context ctx,
                NeoValueOwnership ownership,
                string? memberId)
            {
                // A dropped node no longer hears invalidations for its id.
                if (node is not { live: true })
                    return null;
                for (UnwrapMemo? memo = node.unwrapMemo; memo is not null; memo = memo.next)
                {
                    if (ReferenceEquals(memo.row, row)
                        && ReferenceEquals(memo.cache, ctx.rowUnwrapCache)
                        && memo.ownership == ownership
                        && SameId(memo.memberId, memberId))
                    {
                        return memo;
                    }
                }
                return null;
            }

            internal static void Remember(
                NeoValueNode? node,
                MemberValue row,
                Context ctx,
                NeoValueOwnership ownership,
                string? memberId,
                object? value)
            {
                if (node is not { live: true })
                    return;
                // Entries of a replaced cache can never match again.
                UnwrapMemo? memos = node.unwrapMemo;
                if (memos is not null && !ReferenceEquals(memos.cache, ctx.rowUnwrapCache))
                    memos = null;
                if ((memos?.count ?? 0) < MaxEntries)
                    node.unwrapMemo = new UnwrapMemo(ctx.rowUnwrapCache, row, ownership, memberId, value, memos);
            }

            internal static void Forget(Context ctx, string rowId)
            {
                if (ctx.client.ExistingValueNode(rowId) is { } node)
                    node.unwrapMemo = null;
            }
        }

        internal static void InvalidateCachedCollection(
            string rowId, NeoValueOwnership ownership, Context ctx)
        {
            RowKey rowKey = RowCacheRowKey(ownership, rowId);
            if (!ctx.rowCacheKeysByRow.TryGetValue(rowKey, out HashSet<RowCacheKey>? keys))
                return;
            foreach (RowCacheKey key in keys)
                ctx.rowUnwrapCache.Remove(key);
            ctx.rowCacheKeysByRow.Remove(rowKey);
            UnwrapMemo.Forget(ctx, rowId);
            // Existing aliases keep their reverse provenance and resolve the
            // current membership the next time a variable is evaluated.
        }

        /// <summary>
        /// Keeps the per-evaluation row cache coherent after a mutation-capable
        /// NeoScript frame writes a row. Object-shaped rows are patched in
        /// place so an already-bound <c>this</c> / nested Class receiver keeps
        /// both its identity and its updated child ids. Fixed-size arrays are
        /// patched when possible; values whose CLR shape cannot be updated in
        /// place are evicted so the next read materialises the new row value.
        /// </summary>
        // One refresh runs per changed row on every commit, so the walk
        // reuses these collections. A refresh nested inside another (a patch
        // never re-enters, but the caller's handlers may) gets fresh ones.
        private static readonly List<RowCacheKey> refreshKeyScratch = new();
        private static readonly List<object> refreshAliasScratch = new();
        private static readonly HashSet<object> refreshPatchedScratch = new(ReferenceEqualityComparer.Instance);
        private static bool refreshScratchInUse;

        internal static void RefreshCachedRowAfterWrite(
            MemberValue row,
            Context ctx,
            NeoValueOwnership ownership)
        {
            RowKey rowCacheKey = RowCacheRowKey(ownership, row.id);
            ctx.rowCacheKeysByRow.TryGetValue(
                rowCacheKey,
                out HashSet<RowCacheKey>? indexedKeys);
            bool pooled = !refreshScratchInUse;
            List<RowCacheKey> matchingKeys = pooled ? refreshKeyScratch : new();
            List<object> aliases = pooled ? refreshAliasScratch : new();
            HashSet<object> patchedObjects = pooled ? refreshPatchedScratch : new(ReferenceEqualityComparer.Instance);
            if (pooled)
                refreshScratchInUse = true;
            try
            {
                if (indexedKeys is not null)
                    matchingKeys.AddRange(indexedKeys);
                ctx.rowAliases.GetInto(ownership, row.id, aliases);
                if (matchingKeys.Count == 0 && aliases.Count == 0)
                    return;

                for (int index = 0; index < matchingKeys.Count; index++)
                {
                    RowCacheKey key = matchingKeys[index];
                    if (!ctx.rowUnwrapCache.TryGetValue(key, out object? cached))
                    {
                        indexedKeys!.Remove(key);
                        continue;
                    }
                    if (PatchCachedShape(row, cached))
                    {
                        if (cached is not null)
                            patchedObjects.Add(cached);
                        continue;
                    }

                    ctx.rowUnwrapCache.Remove(key);
                    indexedKeys!.Remove(key);
                    UnwrapMemo.Forget(ctx, row.id);
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
                for (int index = 0; index < aliases.Count; index++)
                {
                    object alias = aliases[index];
                    if (!ctx.rowReverseIndex.TryGetValue(alias, out var reference))
                        continue;
                    if (reference.classId != row.classId)
                    {
                        SetRowReference(ctx, alias, new RowReference(
                            row.id, ownership, row.classId, reference.member));
                    }
                    if (!patchedObjects.Contains(alias))
                        PatchCachedShape(row, alias);
                }
            }
            finally
            {
                if (pooled)
                {
                    matchingKeys.Clear();
                    aliases.Clear();
                    patchedObjects.Clear();
                    refreshScratchInUse = false;
                }
            }
        }

        private static bool PatchCachedShape(MemberValue row, object? cached)
        {
            if (row is ObjectMemberValue { value: not null } objectRow
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
                if (ReferenceEquals(vector3Next, vector3Cached))
                    return true;
                if (vector3Next is null)
                    return false;
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
                if (ReferenceEquals(vector2Next, vector2Cached))
                    return true;
                if (vector2Next is null)
                    return false;
                vector2Cached.x = vector2Next.x;
                vector2Cached.y = vector2Next.y;
                return true;
            }
            if (row is ColorMemberValue colorRow
                && cached is NeoColorValue colorCached)
            {
                NeoColorValue? colorNext = colorRow.value;
                if (ReferenceEquals(colorNext, colorCached))
                    return true;
                if (colorNext is null)
                    return false;
                colorCached.r = colorNext.r;
                colorCached.g = colorNext.g;
                colorCached.b = colorNext.b;
                colorCached.a = colorNext.a;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retargets the cached CLR aliases of <paramref name="movedRowIds"/>,
        /// which were atomically moved from one writable store to another.
        /// Row ids remain stable during a Session-to-Save promotion; only
        /// provenance changes.
        /// </summary>
        internal static void RetargetCachedRowsAfterMove(
            Context ctx,
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership,
            IEnumerable<string> movedRowIds)
        {
            if (sourceOwnership == targetOwnership)
                return;
            var aliases = new List<object>();
            foreach (string rowId in movedRowIds)
            {
                if (ctx.client.HasWritableValue(sourceOwnership, rowId)
                    || !ctx.client.HasWritableValue(targetOwnership, rowId))
                    continue;
                aliases.Clear();
                ctx.rowAliases.GetInto(sourceOwnership, rowId, aliases);
                foreach (object alias in aliases)
                {
                    if (ctx.rowReverseIndex.TryGetValue(alias, out RowReference row) && row.ownership == sourceOwnership)
                        SetRowReference(ctx, alias, new RowReference(rowId, targetOwnership, row.classId, row.member));
                }
                RetargetCachedRow(
                    ctx,
                    sourceOwnership,
                    rowId,
                    targetOwnership,
                    rowId);
            }
        }

        /// <summary>
        /// Retargets the cached CLR aliases of row <paramref name="sourceId"/>,
        /// whose root a shadow import moved onto the leaf at
        /// <paramref name="targetId"/>, and the detached object that attached
        /// to it. A local that held the assigned value then reads and writes
        /// the leaf, as the C# reference does, in this execution and every
        /// later one.
        /// </summary>
        /// <param name="assigned">The value the write assigned.</param>
        /// <returns>The detached object that now names the leaf, or null.</returns>
        internal static NeoScriptObject? RetargetCachedRowAfterShadow(
            Context ctx,
            object? assigned,
            NeoValueOwnership sourceOwnership,
            string sourceId,
            NeoValueOwnership targetOwnership,
            string targetId)
        {
            NeoScriptObject? moved = assigned switch
            {
                NeoScriptObject detached => detached,
                NeoGeneratedClassValue { PendingValue: NeoScriptObject pending } => pending,
                NeoObjectRecord record => record.attachedObject,
                _ => null,
            };
            // Only the object whose own row moved follows it; an owned one keeps its id.
            if (moved is not null && SameId(moved.attachedId, sourceId))
                moved.attachedId = targetId;
            else
                moved = null;
            var aliases = new List<object>();
            ctx.rowAliases.GetInto(sourceOwnership, sourceId, aliases);
            foreach (object alias in aliases)
            {
                if (!ctx.rowReverseIndex.TryGetValue(alias, out RowReference row) || row.ownership != sourceOwnership)
                    continue;
                SetRowReference(ctx, alias, new RowReference(targetId, targetOwnership, row.classId, row.member));
                // A record names its row by id, which a shadow changes.
                if (alias is NeoObjectRecord record && record.valueId == sourceId)
                    record.Rebind(targetId, targetOwnership);
            }
            RetargetCachedRow(ctx, sourceOwnership, sourceId, targetOwnership, targetId);
            return moved;
        }

        private static void RetargetCachedRow(
            Context ctx,
            NeoValueOwnership sourceOwnership,
            string sourceId,
            NeoValueOwnership targetOwnership,
            string targetId)
        {
            RowKey sourceRowKey = RowCacheRowKey(sourceOwnership, sourceId);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    sourceRowKey,
                    out HashSet<RowCacheKey>? sourceKeys))
            {
                return;
            }
            UnwrapMemo.Forget(ctx, sourceId);
            if (!SameId(sourceId, targetId))
                UnwrapMemo.Forget(ctx, targetId);
            RowKey targetRowKey = RowCacheRowKey(targetOwnership, targetId);
            if (!ctx.rowCacheKeysByRow.TryGetValue(
                    targetRowKey,
                    out HashSet<RowCacheKey>? targetKeys))
            {
                targetKeys = new HashSet<RowCacheKey>();
                ctx.rowCacheKeysByRow[targetRowKey] = targetKeys;
            }
            foreach (RowCacheKey sourceKey in sourceKeys.ToArray())
            {
                RowCacheKey targetKey = new RowCacheKey(targetOwnership, targetId, sourceKey.memberId);
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
            foreach (string rowId in rowIds)
                EvictCachedRow(ctx, ownership, rowId);
        }

        /// <summary>
        /// Whether a write to the row may need <see cref="RefreshCachedRowAfterWrite"/>
        /// or <see cref="EvictCachedRow"/>: the context unwrapped it, or an
        /// alias may name it. An alias index no write built yet may hold any.
        /// </summary>
        internal static bool MayCacheRow(Context ctx, NeoValueOwnership ownership, string rowId)
        {
            RowKey key = RowCacheRowKey(ownership, rowId);
            return ctx.rowCacheKeysByRow.ContainsKey(key)
                || ctx.BuiltRowAliases is not { } aliases
                || aliases.Contains(key);
        }

        internal static void EvictCachedRow(Context ctx, NeoValueOwnership ownership, string rowId)
        {
            RowKey rowKey = RowCacheRowKey(ownership, rowId);
            if (ctx.rowCacheKeysByRow.TryGetValue(rowKey, out HashSet<RowCacheKey>? cacheKeys))
            {
                foreach (RowCacheKey cacheKey in cacheKeys)
                    ctx.rowUnwrapCache.Remove(cacheKey);
                ctx.rowCacheKeysByRow.Remove(rowKey);
                UnwrapMemo.Forget(ctx, rowId);
            }
            foreach (object alias in ctx.rowAliases.Get(ownership, rowId))
                RemoveRowReference(ctx, alias);
            ctx.rowAliases.Remove(ownership, rowId);
        }

        private static string OwnershipName(NeoValueOwnership ownership) => ownership switch
        {
            NeoValueOwnership.Asset => "Asset",
            NeoValueOwnership.Save => "Save",
            NeoValueOwnership.Session => "Session",
            _ => ownership.ToString(),
        };

        private static RowKey RowCacheRowKey(
            NeoValueOwnership ownership,
            string rowId) =>
            new RowKey(ownership, rowId);

        private static RowCacheKey MakeRowCacheKey(
            NeoValueOwnership ownership,
            string rowId,
            JsonMember? member = null) =>
            new RowCacheKey(ownership, rowId, member?.id);

        internal static string? ResolveStringValue(
            StringMemberValue value,
            StringMember member,
            Context ctx)
        {
            if (value.value == null)
                return null;
            if (member.Format == NeoStringFormatKind.Plain)
                return value.value;
            if (value.neoLocalizationMode == NeoStringLocalizationMode.Literal)
                return value.value;
            // A member read has no format arguments: hand back the localized
            // template as-is. Running the formatter here can only succeed on
            // placeholder-free text and warns (with a stack capture) on every
            // read of a template that expects arguments.
            return ctx.client.Localization.ResolveTextTemplate(value.value);
        }

        // Always a new array, even when empty: an unwrapped List is keyed by
        // reference, and a shared empty array would alias every empty row.
        private static object?[] ToObjectArray(IReadOnlyList<string> arr)
        {
            var result = new object?[arr.Count];
            for (int i = 0; i < arr.Count; i++)
                result[i] = arr[i];
            return result;
        }

        // Re-implements the dictionary interfaces so every mutation through
        // them drops the child slots; see StoredSlot.
        private sealed class NeoObjectRecord
            : Dictionary<string, object?>, IDictionary<string, object?>, IDictionary, INeoValueReference
        {
            public string? valueId
            {
                get; private set;
            }
            public NeoValueOwnership valueOwnership
            {
                get; private set;
            }

            /// <summary>The detached object that attached to this record's row, if one did.</summary>
            internal NeoScriptObject? attachedObject;

            /// <summary>Names the leaf a shadow moved this record's row onto, as does the object that attached to it.</summary>
            internal void Rebind(string id, NeoValueOwnership ownership)
            {
                valueId = id;
                valueOwnership = ownership;
                if (attachedObject is not null)
                    attachedObject.attachedId = id;
            }

            /// <summary>
            /// This record's entry in <see cref="referenceIndex"/>, so a read
            /// finds its row without the weak-table lookup.
            /// </summary>
            internal RowReference? reference;
            internal ConditionalWeakTable<object, RowReference>? referenceIndex;

            private struct ChildSlot
            {
                internal MergedSchemaEntry entry;
                internal string id;
                internal NeoValueNode node;
                // The virtual-children epoch an absent member's child id was
                // resolved at; -1 for an id the member stores.
                internal int virtualEpoch;
            }

            // The value nodes of child ids read through schema members, so a
            // repeated member read skips the id lookup. A slot holds while
            // the member still stores that exact id string and its node lives,
            // or, for an absent member's virtual child, while the client's
            // virtual children haven't moved.
            private ChildSlot[]? children;
            private int childCount;

            /// <summary>
            /// The slot holding <paramref name="entry"/>'s stored child, or -1,
            /// so a repeat member read skips hashing its id. Any mutation drops
            /// every slot, so a stored slot's id is still the record's value.
            /// </summary>
            internal int StoredSlot(MergedSchemaEntry entry)
            {
                for (int i = 0; i < childCount; i++)
                {
                    if (ReferenceEquals(children![i].entry, entry))
                        return children[i].virtualEpoch < 0 ? i : -1;
                }
                return -1;
            }

            internal string StoredId(int slot) => children![slot].id;

            internal NeoValueNode StoredNode(int slot) => children![slot].node;

            /// <summary><see cref="RememberChildNode"/> for the slot <see cref="StoredSlot"/> found.</summary>
            internal void RememberStoredNode(int slot, NeoValueNode? node)
            {
                if (node is { live: true } && !ReferenceEquals(children![slot].node, node))
                    children[slot].node = node;
            }

            object? IDictionary<string, object?>.this[string key]
            {
                get => this[key];
                set
                {
                    childCount = 0;
                    this[key] = value;
                }
            }

            void IDictionary<string, object?>.Add(string key, object? value)
            {
                childCount = 0;
                Add(key, value);
            }

            bool IDictionary<string, object?>.Remove(string key)
            {
                childCount = 0;
                return Remove(key);
            }

            void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
            {
                childCount = 0;
                Add(item.Key, item.Value);
            }

            bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
            {
                if (!TryGetValue(item.Key, out object? value)
                    || !EqualityComparer<object?>.Default.Equals(value, item.Value))
                {
                    return false;
                }
                childCount = 0;
                return Remove(item.Key);
            }

            void ICollection<KeyValuePair<string, object?>>.Clear()
            {
                childCount = 0;
                Clear();
            }

            object? IDictionary.this[object key]
            {
                get => key is string text && TryGetValue(text, out object? value) ? value : null;
                set
                {
                    childCount = 0;
                    this[(string)key] = value;
                }
            }

            void IDictionary.Add(object key, object? value)
            {
                childCount = 0;
                Add((string)key, value);
            }

            void IDictionary.Remove(object key)
            {
                if (key is not string text)
                    return;
                childCount = 0;
                Remove(text);
            }

            void IDictionary.Clear()
            {
                childCount = 0;
                Clear();
            }

            internal NeoValueNode? ChildNode(MergedSchemaEntry entry, object? id)
            {
                for (int i = 0; i < childCount; i++)
                {
                    ref ChildSlot slot = ref children![i];
                    if (ReferenceEquals(slot.entry, entry))
                        return ReferenceEquals(slot.id, id) ? slot.node : null;
                }
                return null;
            }

            /// <summary>
            /// The virtual child id an absent member resolved to at
            /// <paramref name="virtualEpoch"/>, or null.
            /// </summary>
            internal string? VirtualChildId(MergedSchemaEntry entry, int virtualEpoch)
            {
                if (virtualEpoch < 0)
                    return null;
                for (int i = 0; i < childCount; i++)
                {
                    ref ChildSlot slot = ref children![i];
                    if (ReferenceEquals(slot.entry, entry))
                        return slot.virtualEpoch == virtualEpoch && slot.node.live ? slot.id : null;
                }
                return null;
            }

            /// <param name="virtualEpoch">For an absent member's virtual child, the epoch it was resolved at.</param>
            internal void RememberChildNode(MergedSchemaEntry entry, object? id, NeoValueNode? node, int virtualEpoch = -1)
            {
                if (id is not string childId || node is not { live: true })
                    return;
                for (int i = 0; i < childCount; i++)
                {
                    ref ChildSlot slot = ref children![i];
                    if (!ReferenceEquals(slot.entry, entry))
                        continue;
                    // A repeat read finds its own slot unchanged.
                    if (!ReferenceEquals(slot.node, node) || !ReferenceEquals(slot.id, childId))
                    {
                        slot.id = childId;
                        slot.node = node;
                    }
                    slot.virtualEpoch = virtualEpoch;
                    return;
                }
                children ??= new ChildSlot[4];
                if (childCount == children.Length)
                    Array.Resize(ref children, childCount * 2);
                children[childCount++] = new ChildSlot { entry = entry, id = childId, node = node, virtualEpoch = virtualEpoch };
            }

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
            foreach (var kvp in dict)
                result[kvp.Key] = kvp.Value;
            return result;
        }

        private static bool IsPrimitiveArray(Newtonsoft.Json.Linq.JArray entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Type is Newtonsoft.Json.Linq.JTokenType.Array or Newtonsoft.Json.Linq.JTokenType.Object)
                    return false;
            }
            return true;
        }

        private static object?[] CopyEntries(object?[] template)
        {
            var copy = new object?[template.Length];
            for (int i = 0; i < template.Length; i++)
                copy[i] = template[i];
            return copy;
        }

        private static object? UnwrapJToken(Newtonsoft.Json.Linq.JToken? token)
        {
            if (token is null)
                return null;
            switch (token.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.Null:
                case Newtonsoft.Json.Linq.JTokenType.Undefined:
                    return null;
                case Newtonsoft.Json.Linq.JTokenType.Boolean:
                    return token.Value<bool>();
                case Newtonsoft.Json.Linq.JTokenType.Integer:
                    return token.Value<double>();
                case Newtonsoft.Json.Linq.JTokenType.Float:
                    return token.Value<double>();
                case Newtonsoft.Json.Linq.JTokenType.String:
                    return token.Value<string>();
                case Newtonsoft.Json.Linq.JTokenType.Array:
                    {
                        var arr = (Newtonsoft.Json.Linq.JArray)token;
                        var result = new object?[arr.Count];
                        for (int i = 0; i < arr.Count; i++)
                            result[i] = UnwrapJToken(arr[i]);
                        return result;
                    }
                case Newtonsoft.Json.Linq.JTokenType.Object:
                    {
                        var obj = (Newtonsoft.Json.Linq.JObject)token;
                        var result = new Dictionary<string, object?>();
                        foreach (var kvp in obj)
                            result[kvp.Key] = UnwrapJToken(kvp.Value);
                        return result;
                    }
                default:
                    return token.ToString();
            }
        }

        // A return, not an out parameter: Mono write-barriers every
        // reference stored through a byref. Records are the common receiver,
        // and a sealed class test is one compare.
        private static IDictionary<string, object?>? AsObjectRecord(object? value) =>
            value as NeoObjectRecord ?? value as IDictionary<string, object?>;

        // NeoScript numbers are doubles, so that test inlines into the caller.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryAsDouble(object? value, out double result)
        {
            if (value is double d)
            {
                result = d;
                return true;
            }
            return TryAsOtherDouble(value, out result);
        }

        private static bool TryAsOtherDouble(object? value, out double result)
        {
            // Objects, arrays and strings, the common non-numbers, need one test.
            if (value is not ValueType)
            {
                result = 0;
                return false;
            }
            switch (value)
            {
                case float f:
                    result = f;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case short sh:
                    result = sh;
                    return true;
                case decimal dec:
                    result = (double)dec;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static bool TryReadVectorComponent(
            object? value,
            string key,
            out float component)
        {
            component = 0;
            if (value is Vector2 vector2 && value is not Vector3)
            {
                if (key == "x")
                {
                    component = vector2.x;
                    return true;
                }
                if (key == "y")
                {
                    component = vector2.y;
                    return true;
                }
                return false;
            }
            if (value is Vector2Int vector2Int && value is not Vector3Int)
            {
                if (key == "x")
                {
                    component = vector2Int.x;
                    return true;
                }
                if (key == "y")
                {
                    component = vector2Int.y;
                    return true;
                }
                return false;
            }
            if (value is Vector3 vector3)
            {
                if (key == "x")
                {
                    component = vector3.x;
                    return true;
                }
                if (key == "y")
                {
                    component = vector3.y;
                    return true;
                }
                if (key == "z")
                {
                    component = vector3.z;
                    return true;
                }
                return false;
            }
            if (value is Vector3Int vector3Int)
            {
                if (key == "x")
                {
                    component = vector3Int.x;
                    return true;
                }
                if (key == "y")
                {
                    component = vector3Int.y;
                    return true;
                }
                if (key == "z")
                {
                    component = vector3Int.z;
                    return true;
                }
                return false;
            }
            if (value is NeoVector2Value v2)
            {
                if (key == "x")
                {
                    component = v2.x;
                    return true;
                }
                if (key == "y")
                {
                    component = v2.y;
                    return true;
                }
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
                if (!isVector2 && !isVector3)
                    return false;
                if (key == "z" && !isVector3)
                    return false;
                if (key != "x" && key != "y" && key != "z")
                    return false;
                if (!dict.TryGetValue(key, out var raw))
                    return false;
                if (!TryAsDouble(raw, out double numeric))
                    return false;
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
                if (!dict.TryGetValue(key, out var raw))
                    return false;
                if (!TryAsDouble(raw, out double numeric))
                    return false;
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
            if (value is Vector2Int && value is not Vector3Int)
                return true;
            if (value is NeoReadOnlyVector2 wrapper)
            {
                Vector2 vector = wrapper.Value;
                return !requireIntegers || (IsInteger(vector.x) && IsInteger(vector.y));
            }
            if (value is NeoReadOnlyVector2Int)
                return true;
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
            if (value is Vector3Int)
                return true;
            if (value is NeoReadOnlyVector3 wrapper)
            {
                Vector3 vector = wrapper.Value;
                return !requireIntegers ||
                    (IsInteger(vector.x) && IsInteger(vector.y) && IsInteger(vector.z));
            }
            if (value is NeoReadOnlyVector3Int)
                return true;
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
            if (value is Color)
                return true;
            if (value is NeoReadOnlyColor)
                return true;
            if (value is NeoColorValue)
                return true;
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
            return NeoNumbers.IsWhole(value);
        }

        private static string ReceiverTypeName(object? receiver)
        {
            if (receiver is null)
                return "null";
            if (receiver is string)
                return "string";
            if (receiver is bool)
                return "boolean";
            if (TryAsDouble(receiver, out _))
                return "number";
            if (receiver is object?[])
                return "array";
            if (receiver is IDictionary<string, object?>)
                return "object";
            return receiver.GetType().Name;
        }

        // ---------------------------------------------------------------
        // Reference-equality lookups against the project's value rows.
        // Mirrors the TS-side reliance on `ctx.vm.values.find(r => r.value === value)`.
        // ---------------------------------------------------------------

        /// <param name="row">The row <paramref name="value"/> was unwrapped from, when the caller just read it.</param>
        internal static string? FindRowClassIdByReference(object? value, Context ctx, MemberValue? row = null)
        {
            // The row the value came from names its class, as its reverse
            // index entry would.
            if (!string.IsNullOrEmpty(row?.classId))
                return row!.classId;
            if (value is NeoScriptObject { attachedId: null } detached)
                return detached.plan.classId;
            // Prefer the context's exact ownership-qualified reverse index.
            // The same stable id may legitimately exist in Session and Save
            // with different runtime classes; id-only lookup would select the
            // wrong overlay for an unwrapped NeoObjectRecord.
            if (FindRowReference(value, ctx) is { } rowRef)
            {
                return ClassIdOfRowReference(rowRef, ctx, row);
            }
            return FindReferencedClassId(value, ctx);
        }

        /// <summary>
        /// Whether <paramref name="value"/>'s runtime Class is
        /// <paramref name="expectedClassId"/> or extends it: the
        /// <see cref="NeoClient.ClassChainContains"/> test on
        /// <see cref="FindRowClassIdByReference"/>, answered from the class
        /// node a row's reference keeps.
        /// </summary>
        internal static bool RuntimeClassExtends(object? value, string expectedClassId, Context ctx)
        {
            if (value is not NeoScriptObject { attachedId: null } && FindRowReference(value, ctx) is { } rowRef)
            {
                string? classId = ClassIdOfRowReference(rowRef, ctx);
                if (string.IsNullOrEmpty(classId))
                    return false;
                return classId == expectedClassId || ClassNodeExtends(rowRef, classId!, expectedClassId, ctx);
            }
            string? runtimeClassId = FindRowClassIdByReference(value, ctx);
            return !string.IsNullOrEmpty(runtimeClassId)
                && ctx.client.ClassChainContains(runtimeClassId!, expectedClassId);
        }

        // Its own method so the exception region stays off the caller's frame.
        private static bool ClassNodeExtends(RowReference rowRef, string classId, string expectedClassId, Context ctx)
        {
            try
            {
                return rowRef.ClassNode(ctx.client, classId).Extends(expectedClassId);
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
        }

        /// <param name="row">The row <paramref name="rowRef"/> names, when the caller just read it.</param>
        private static string? ClassIdOfRowReference(RowReference rowRef, Context ctx, MemberValue? row = null)
        {
            {
                if ((row ?? ctx.client.ReadReplayReference(
                        rowRef.valueId,
                        ref rowRef.node,
                        rowRef.ownership)) is not { } indexedRow)
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
                return rowRef.classId ?? (ctx.client.TryInferMemberForValueId(
                        rowRef.valueId,
                        out JsonMember? indexedMember)
                    && indexedMember is ClassMember indexedClassMember
                        ? indexedClassMember.classId
                        : null);
            }
        }

        private static string? FindReferencedClassId(object? value, Context ctx)
        {
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
                if (!string.IsNullOrEmpty(referencedRow.classId))
                    return referencedRow.classId;
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
            foreach (NeoSchemaClass schemaClass in client.classes.Values)
                yield return schemaClass;
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
            foreach (var kvp in client.members)
                yield return kvp;
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
                if (seen.Contains(kvp.Key))
                    continue;
                yield return kvp;
            }
        }

        // ---------------------------------------------------------------
        // String formatting for `$"..."` interpolation
        // ---------------------------------------------------------------

        private static string FormatForInterp(object? value, TypeInfo sourceType, Context ctx)
        {
            if (value is null)
                return "";
            if (sourceType is LookupTypeInfo { entryTypeInfo: EnumTypeInfo enumType })
            {
                sourceType = enumType;
            }
            switch (sourceType.type)
            {
                case MemberKind.Enum:
                    {
                        string enumId = (sourceType as EnumTypeInfo)?.enumId ?? "";
                        var ids = new List<string>();
                        if (value is object?[] arr)
                        {
                            foreach (var e in arr)
                                if (e is string s)
                                    ids.Add(s);
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
                case MemberKind.Lookup:
                    {
                        var entryType = (sourceType as LookupTypeInfo)?.entryTypeInfo;
                        string entryName = entryType is null ? "unknown" : DescribeRuntimeType(entryType, ctx);
                        string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                        return $"(Set<{entryName}>, Value<{rowId}>)";
                    }
                default:
                    return value.ToString() ?? "";
            }
        }

        private static string DescribeRuntimeType(TypeInfo t, Context ctx)
        {
            switch (t.type)
            {
                case MemberKind.Null:
                    return "null";
                case MemberKind.Bool:
                    return "bool";
                case MemberKind.Int:
                    return "int";
                case MemberKind.Float:
                    return "float";
                case MemberKind.String:
                    return "string";
                case MemberKind.Sprite:
                    return "SpriteInfo";
                case MemberKind.Audio:
                    return "AudioClipInfo";
                case MemberKind.Vector2:
                    return "Vector2";
                case MemberKind.Vector2Int:
                    return "Vector2Int";
                case MemberKind.Vector3:
                    return "Vector3";
                case MemberKind.Vector3Int:
                    return "Vector3Int";
                case MemberKind.Color:
                    return "Color";
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
                case MemberKind.Lookup:
                    {
                        var inner = (t as LookupTypeInfo)?.entryTypeInfo;
                        return inner is null ? "Set<unknown>" : $"Set<{DescribeRuntimeType(inner, ctx)}>";
                    }
                default:
                    return "unknown";
            }
        }

        internal static string? FindRowIdByReference(object? value, Context ctx)
        {
            return FindRowReference(value, ctx)?.valueId;
        }

        /// <summary>
        /// The declared entry member of a collection's entries: a List or
        /// Dictionary row's own, or its source's for a Where result.
        /// </summary>
        private static JsonMember? CollectionEntryMember(
            RowReference? collectionRef,
            object? collection,
            Context ctx) =>
            collectionRef is not null
                ? collectionRef.EntryMember(ctx.client)
                : collection is not null && DerivedEntryMembers.TryGetValue(collection, out JsonMember? entryMember)
                    ? entryMember
                    : null;

        internal static JsonMember? CollectionEntryMember(object? collection, Context ctx) =>
            CollectionEntryMember(FindRowReference(collection, ctx), collection, ctx);

        /// <summary>
        /// <paramref name="value"/> as C# reads it. A stored List or
        /// Dictionary, or a query over one, holds its entries as row ids only
        /// the evaluator resolves, so it becomes a copy of its entry values.
        /// </summary>
        internal static object? ResolveHostCollection(object? value, Context ctx)
        {
            if (value is not (object?[] or Dictionary<string, object?>))
                return value;
            RowReference? rowRef = FindRowReference(value, ctx);
            JsonMember? entryMember;
            if (rowRef is not null)
            {
                if (rowRef.CollectionMember(ctx.client) is not (ListMember or DictionaryMember))
                    return value;
                entryMember = rowRef.EntryMember(ctx.client);
            }
            // A class record is never a derived collection.
            else if (value is NeoObjectRecord || !DerivedEntryMembers.TryGetValue(value, out entryMember))
                return value;
            bool nested = entryMember is null or ListMember or DictionaryMember;
            NeoValueOwnership? ownership = rowRef?.ownership;
            if (value is object?[] rows)
            {
                var entries = new object?[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    object? entry = ResolveListEntry(rows, i, rowRef, ownership, ctx, entryMember);
                    entries[i] = nested ? ResolveHostCollection(entry, ctx) : entry;
                }
                return entries;
            }
            var stored = (Dictionary<string, object?>)value;
            var values = new Dictionary<string, object?>(stored.Count, stored.Comparer);
            foreach (KeyValuePair<string, object?> pair in stored)
            {
                object? entry = ResolveValueIfId(pair.Value, ctx, ownership, entryMember);
                values.Add(pair.Key, nested ? ResolveHostCollection(entry, ctx) : entry);
            }
            return values;
        }

        /// <summary>
        /// <paramref name="args"/> with each collection resolved by
        /// <see cref="ResolveHostCollection"/>: in place when the caller
        /// <paramref name="owned"/> the array, otherwise in a copy it then owns.
        /// </summary>
        internal static object?[] ResolveHostArguments(object?[] args, Context ctx, ref bool owned)
        {
            for (int i = 0; i < args.Length; i++)
            {
                object? resolved = ResolveHostCollection(args[i], ctx);
                if (ReferenceEquals(resolved, args[i]))
                    continue;
                if (!owned)
                {
                    args = (object?[])args.Clone();
                    owned = true;
                }
                args[i] = resolved;
            }
            return args;
        }

        /// <summary>
        /// Gives a collection derived from another's value ids that source's
        /// entry member. An empty array has no entries to resolve and can be
        /// a shared instance (<c>List.ToArray</c> returns one), so it is skipped.
        /// </summary>
        internal static void KeepEntryMember(object derived, JsonMember? entryMember)
        {
            if (entryMember is null || derived is object?[] { Length: 0 })
                return;
            DerivedEntryMembers.Add(derived, entryMember);
        }

        /// <summary>
        /// Gives <paramref name="copy"/> the entry member
        /// <paramref name="source"/> derived, so a copy of a Where result
        /// still resolves its entries.
        /// </summary>
        internal static void KeepEntryMemberOf(object source, object copy)
        {
            if (DerivedEntryMembers.TryGetValue(source, out JsonMember? entryMember))
                KeepEntryMember(copy, entryMember);
        }

        internal static JsonMember? FindRowMemberByReference(object? value, Context ctx) =>
            FindRowReference(value, ctx)?.member;

        internal static NeoValueOwnership? FindRowOwnershipByReference(object? value, Context ctx) =>
            FindRowOwnershipByReference(value, ctx, out _);

        internal static NeoValueOwnership? FindRowOwnershipByReference(object? value, Context ctx, out RowReference? rowRef)
        {
            if (value is NeoScriptObject { attachedId: null })
            {
                rowRef = null;
                return NeoValueOwnership.Session;
            }
            rowRef = FindRowReference(value, ctx);
            return RowOwnership(rowRef, value);
        }

        /// <summary>
        /// Entry <paramref name="index"/> of a row-backed list, read through
        /// the list row's entry nodes the way a cursor reads it, so a repeat
        /// read skips the id lookup.
        /// </summary>
        internal static object? ResolveListEntry(
            object?[] rows,
            int index,
            RowReference? listRef,
            NeoValueOwnership? ownership,
            Context ctx,
            JsonMember? member = null)
        {
            object? raw = rows[index];
            NeoValueNode? node = listRef?.EntryNode(index, raw);
            object? entry = ResolveValueIfId(raw, ctx, ownership, member, ref node);
            listRef?.RememberEntryNode(index, rows.Length, raw, node);
            return entry;
        }

        // Row-backed arguments can cross evaluator contexts. Their original
        // reverse index is then unavailable, but the record still carries
        // the exact selected store, including sparse authored fallbacks.
        internal static NeoValueOwnership? RowOwnership(RowReference? rowRef, object? value) =>
            rowRef?.ownership ?? (value as NeoObjectRecord)?.valueOwnership;

        /// <summary>Unwraps a memoized row result the way the evaluation that produced it did.</summary>
        internal static object? UnwrapMemoizedRow(MemberValue row, Context ctx, RowReference reference) =>
            UnwrapCached(row, ctx, reference.ownership, reference.member, reference.node);

        /// <summary>
        /// What a getter memo keeps of a derived list result: each scalar entry
        /// as is and each row entry as its reference. Null for a row's own
        /// list, an array a copy would detach from its origin, or an entry
        /// another evaluation context cannot re-resolve.
        /// </summary>
        internal static object?[]? MemoizableList(object?[] entries, Context ctx, out JsonMember? entryMember)
        {
            entryMember = null;
            if (FindRowReference(entries, ctx) is not null
                || NeoGeneratedTypesSupport.TryGetDetachedArrayOrigin(entries, out _)
                || NeoGeneratedTypesSupport.HasConstructorCollectionOrigin(entries))
                return null;
            var recipe = new object?[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                object? entry = entries[i];
                if (entry is null or string or bool or double or int or long or float)
                    recipe[i] = entry;
                else if (entry is not NeoScriptObject { attachedId: null }
                    && FindRowReference(entry, ctx) is { } entryRef
                    && entryRef.ownership != NeoValueOwnership.Session)
                    recipe[i] = entryRef;
                else
                    return null;
            }
            DerivedEntryMembers.TryGetValue(entries, out entryMember);
            return recipe;
        }

        /// <summary>
        /// Rebuilds a memoized list into a fresh array the caller owns, or
        /// null when one of its rows is gone. With <paramref name="lendingMemberId"/>,
        /// an array with no entry member is lent out: a foreach over that
        /// getter's call hands it back through <see cref="RecycleMemoizedList"/>
        /// once it has taken its snapshot.
        /// </summary>
        internal static object?[]? ResolveMemoizedList(
            object?[] recipe,
            JsonMember? entryMember,
            Context ctx,
            string? lendingMemberId = null)
        {
            bool lend = lendingMemberId is not null && entryMember is null;
            var entries = lend
                ? TemporaryLists.Rent(recipe.Length)
                : new object?[recipe.Length];
            for (int i = 0; i < recipe.Length; i++)
            {
                if (recipe[i] is not RowReference entryRef)
                {
                    entries[i] = recipe[i];
                    continue;
                }
                if (ctx.client.ReadReplayReference(entryRef.valueId, ref entryRef.node, entryRef.ownership) is not { } row)
                    return null;
                entries[i] = UnwrapCached(row, ctx, entryRef.ownership, entryRef.member, entryRef.node);
            }
            if (lend)
                TemporaryLists.Lend(entries, lendingMemberId!);
            else
                KeepEntryMember(entries, entryMember);
            return entries;
        }

        /// <summary>
        /// Takes back the array a memoized <paramref name="memberId"/> hit
        /// just lent, when <paramref name="collection"/> is that array. The
        /// hit returns straight to its caller, so a getter call consumed only
        /// for its entries is the array's one holder.
        /// </summary>
        internal static void RecycleMemoizedList(object? collection, string memberId) =>
            TemporaryLists.ReturnLent(collection, memberId);

        /// <summary>
        /// List arrays only the interpreter holds, pooled per small length and
        /// thread. A memoized getter hit lends one to a foreach; a grid query's
        /// result stays exclusive until anything else can see it, so a Where
        /// over the call filters it in place; and a local the scope layout
        /// proves never escapes returns its array when its body completes.
        /// </summary>
        internal static class TemporaryLists
        {
            private const int MaxPooledLength = 32;

            [ThreadStatic]
            private static object?[]?[]? free;
            [ThreadStatic]
            private static object?[]? lent;
            [ThreadStatic]
            private static string? lentMemberId;
            [ThreadStatic]
            private static object?[]? exclusive;

            internal static object?[] Rent(int length)
            {
                if (length == 0 || length > MaxPooledLength || free?[length] is not object?[] entries)
                    return new object?[length];
                free[length] = null;
                return entries;
            }

            /// <summary>Takes back an array nothing else holds.</summary>
            internal static void Return(object?[] entries)
            {
                if (ReferenceEquals(entries, exclusive))
                    exclusive = null;
                if (entries.Length == 0 || entries.Length > MaxPooledLength)
                    return;
                DerivedEntryMembers.Remove(entries);
                Array.Clear(entries, 0, entries.Length);
                (free ??= new object?[MaxPooledLength + 1][])[entries.Length] = entries;
            }

            internal static void Lend(object?[] entries, string memberId)
            {
                lent = entries;
                lentMemberId = memberId;
            }

            internal static void ReturnLent(object? collection, string memberId)
            {
                if (!ReferenceEquals(collection, lent) || lentMemberId != memberId)
                    return;
                object?[] entries = lent!;
                lent = null;
                lentMemberId = null;
                Return(entries);
            }

            internal static void MarkExclusive(object?[] entries) => exclusive = entries;

            /// <summary>Claims <paramref name="collection"/> when it is the exclusive array.</summary>
            internal static bool TakeExclusive(object? collection)
            {
                if (collection is null || !ReferenceEquals(collection, exclusive))
                    return false;
                exclusive = null;
                return true;
            }

            /// <summary>A value something now retains is no longer exclusive.</summary>
            internal static void Forget(object? value)
            {
                if (ReferenceEquals(value, exclusive))
                    exclusive = null;
            }
        }

        /// <summary>
        /// The stored row an unwrapped value came from, or null. Returned
        /// rather than written through an <c>out</c>: a reference stored
        /// through a byref pays a GC write barrier.
        /// </summary>
        /// <summary>
        /// Whether <paramref name="value"/> has a type the reverse index keys:
        /// the unwraps <see cref="UnwrapCached"/> indexes. Scalars and
        /// detached objects never are, and skip the table's locked lookup.
        /// </summary>
        private static bool MayBeRowAlias(object? value) =>
            value is object?[] or IDictionary<string, object?> or NeoVector2Value or NeoColorValue;

        internal static RowReference? FindRowReference(object? value, Context ctx)
        {
            // A static receiver, among others.
            if (value is null)
                return null;
            if (value is NeoObjectRecord objectRecord)
            {
                // A record carries its own entry: it is indexed exactly when
                // it names this index.
                return ReferenceEquals(objectRecord.referenceIndex, ctx.rowReverseIndex)
                    ? objectRecord.reference
                    : null;
            }
            // A detached object finds its row through the record it forwards to.
            if (value is object?[] or IDictionary<string, object?> and not NeoScriptObject)
                return ctx.CollectionRowReference(value);
            RowReference rowRef;
            if (MayBeRowAlias(value) && ctx.rowReverseIndex.TryGetValue(value!, out rowRef))
                return rowRef;
            // Anything that needs a detached object's row gets it.
            if (value is NeoScriptObject detached
                && ForwardDetached(detached, ctx) is { } record
                && ctx.rowReverseIndex.TryGetValue(record, out rowRef))
                return rowRef;
            object? leafOwner = (value as NeoVector2Value)?.detachedOwner
                ?? (value as NeoColorValue)?.detachedOwner;
            return leafOwner is NeoScriptObject owner && TryFindDetachedLeafRow(value!, owner, ctx, out rowRef)
                ? rowRef
                : null;
        }

        private static System.Text.StringBuilder? concatenation;

        // One reused builder: only the joined string allocates.
        // It is taken while in use, so a nested join builds its own.
        private static string Concatenate(object?[] operands)
        {
            System.Text.StringBuilder builder = concatenation ?? new System.Text.StringBuilder();
            concatenation = null;
            foreach (var o in operands)
                builder.Append(StringifyForInterp(o));
            string joined = builder.ToString();
            builder.Clear();
            concatenation = builder;
            return joined;
        }

        /// <param name="value">An <see cref="EvaluateValue"/> result.</param>
        private static string StringifyOperand(object? value, double number) =>
            ReferenceEquals(value, ArithmeticValue.BareNumber)
                ? NeoNumbers.Format(number)
                : StringifyForInterp(value);

        private static string StringifyForInterp(object? v)
        {
            if (v is null)
                return "";
            if (v is string s)
                return s;
            if (v is bool b)
                return b ? "true" : "false";
            if (TryAsDouble(v, out double d))
                return NeoNumbers.Format(d);
            return Newtonsoft.Json.JsonConvert.SerializeObject(v);
        }
    }
}
