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
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// Pure, stateless walker that evaluates a compiled
    /// <see cref="FunctionWithReturnType"/> NSGetter. C# port of the TS
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
    /// <see cref="NeoAttributeNSGetter.Compute"/>'s try/catch.</para>
    /// </summary>
    public static class NSGetterEvaluator
    {
        /// <summary>
        /// Per-evaluation context: the project, the bound
        /// <c>__this__</c> / <c>__root__</c> values, and a cycle-detection
        /// stack of NSGetter attribute ids currently being evaluated.
        /// </summary>
        public class Context
        {
            public delegate object? NativeFunctionCallHandler(
                CallNativeFunctionPointer pointer,
                Dictionary<string, object?> scope,
                Context ctx);

            public NeoClient client { get; }
            public object? thisValue { get; }
            public object? rootValue { get; }
            public object? contextValue { get; }
            public INeoDialogueMemoryStore? memoryStore { get; }
            /// <summary>
            /// Stack of NSGetter attribute ids currently in-flight. Threaded
            /// through callGetter recursion via fresh-copy children so a
            /// cycle (`A.x` calls `B.y` calls `A.x` on a different receiver)
            /// trips before the runtime stack overflows.
            /// </summary>
            public IReadOnlyCollection<string> getterCallStack { get; }
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
            ///   <see cref="FindRowTypeIdByReference"/> /
            ///   <see cref="FindRowIdByReference"/> matching the
            ///   receiver back to its source row — needed for
            ///   <c>is</c>-checks against Custom types and for
            ///   stringification of Custom / List / Dictionary results.
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
            internal NativeFunctionCallHandler? nativeFunctionCallHandler { get; }

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
                NativeFunctionCallHandler? nativeFunctionCallHandler = null)
            {
                this.client = client;
                this.thisValue = thisValue;
                this.rootValue = rootValue;
                this.contextValue = contextValue;
                this.memoryStore = memoryStore;
                this.getterCallStack = getterCallStack ?? System.Array.Empty<string>();
                this.rowUnwrapCache = rowUnwrapCache ?? new Dictionary<string, object?>();
                this.rowReverseIndex = rowReverseIndex
                    ?? new Dictionary<object, RowReference>(ReferenceEqualityComparer.Instance);
                this.valueOwnership = valueOwnership;
                this.nativeFunctionCallHandler = nativeFunctionCallHandler;
            }

            internal Context WithGetterPushed(string attributeId)
            {
                var next = new HashSet<string>(getterCallStack) { attributeId };
                return new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    next,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    nativeFunctionCallHandler);
            }

            internal Context WithThis(object? newThisValue)
            {
                return new Context(
                    client,
                    newThisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    nativeFunctionCallHandler);
            }

            internal Context WithRoot(object? newRootValue)
            {
                return new Context(
                    client,
                    thisValue,
                    newRootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    nativeFunctionCallHandler);
            }

            internal Context WithContext(object? newContextValue)
            {
                return new Context(
                    client,
                    thisValue,
                    rootValue,
                    newContextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    nativeFunctionCallHandler);
            }

            internal Context WithMemoryStore(INeoDialogueMemoryStore? newMemoryStore)
            {
                return new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    newMemoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    nativeFunctionCallHandler);
            }

            internal Context WithNativeFunctionCallHandler(
                NativeFunctionCallHandler handler)
            {
                return new Context(
                    client,
                    thisValue,
                    rootValue,
                    contextValue,
                    memoryStore,
                    getterCallStack,
                    rowUnwrapCache,
                    rowReverseIndex,
                    valueOwnership,
                    handler);
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

            public RowReference(string valueId, NeoValueOwnership ownership)
            {
                this.valueId = valueId;
                this.ownership = ownership;
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
        public static object? Evaluate(FunctionWithReturnType getter, Context ctx)
        {
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = ctx.thisValue,
                ["__root__"] = ctx.rootValue,
                ["__context__"] = ctx.contextValue,
            };
            var result = EvalInstructions(getter.instructions, scope, ctx);
            if (result.kind == InstructionResultKind.Return) return result.value;
            throw new NSGetterRuntimeError("Function ended without a return statement");
        }

        /// <summary>
        /// Materialises the unwrapped CLR shape for an
        /// <see cref="AttributeValue"/> row through the per-context
        /// cache + reverse index. Public so external callers (notably
        /// <see cref="NeoAttributeNSGetter.Compute"/>) can pre-warm the
        /// cache when binding <c>__this__</c> to a known row — without
        /// going through the cache, <c>is</c>-checks against Custom
        /// types and runtime-override dispatch on the receiver wouldn't
        /// fire because reference equality would never round-trip.
        /// </summary>
        public static object? UnwrapRow(AttributeValue row, Context ctx) =>
            UnwrapCached(row, ctx, ctx.valueOwnership);

        public static object? UnwrapRow(
            AttributeValue row,
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
        // Instructions
        // ---------------------------------------------------------------

        private enum InstructionResultKind { Fallthrough, Return }
        private readonly struct InstructionResult
        {
            public InstructionResultKind kind { get; }
            public object? value { get; }
            public InstructionResult(InstructionResultKind kind, object? value)
            {
                this.kind = kind;
                this.value = value;
            }
            public static InstructionResult Fallthrough() => new(InstructionResultKind.Fallthrough, null);
            public static InstructionResult Return(object? value) => new(InstructionResultKind.Return, value);
        }

        private static InstructionResult EvalInstructions(
            Instruction[] instructions,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            foreach (var ins in instructions)
            {
                switch (ins)
                {
                    case VariableInstruction varInstr:
                    {
                        var v = EvalPointer(varInstr.variable.pointer, scope, ctx);
                        scope[varInstr.variable.id] = v;
                        break;
                    }
                    case IfInstruction ifInstr:
                    {
                        bool matched = false;
                        foreach (var branch in ifInstr.branches)
                        {
                            if (EvalBooleanExpression(branch.expression, scope, ctx))
                            {
                                matched = true;
                                var r = EvalInstructions(branch.instructions, scope, ctx);
                                if (r.kind == InstructionResultKind.Return) return r;
                                break;
                            }
                        }
                        if (!matched && ifInstr.elseInstructions is not null)
                        {
                            var r = EvalInstructions(ifInstr.elseInstructions, scope, ctx);
                            if (r.kind == InstructionResultKind.Return) return r;
                        }
                        break;
                    }
                    case ReturnInstruction retInstr:
                    {
                        object? v = retInstr.pointer is null
                            ? null
                            : EvalPointer(retInstr.pointer, scope, ctx);
                        return InstructionResult.Return(v);
                    }
                    case ThrowInstruction throwInstr:
                    {
                        var msg = EvalPointer(throwInstr.pointer, scope, ctx);
                        throw new NSGetterRuntimeError(msg?.ToString() ?? "null");
                    }
                    case NativeCallInstruction nativeCall:
                    {
                        EvalNativeFunctionCall(nativeCall.call, scope, ctx);
                        break;
                    }
                    case AssignInstruction assign:
                    {
                        // Getters are pure: only LOCAL variable reassignment
                        // (`found = found + 1`) is legal here. Attribute writes
                        // belong to dialogue actions (NeoDialogueActionEvaluator).
                        if (assign.target.pointer is not VariablePointer variablePointer)
                        {
                            throw new NSGetterRuntimeError(
                                "Getters cannot assign to attributes; assignment targets must be local variables.");
                        }
                        scope[variablePointer.variableId] =
                            EvalPointer(assign.pointer, scope, ctx);
                        break;
                    }
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown instruction kind {ins.GetType().Name}");
                }
            }
            return InstructionResult.Fallthrough();
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
                    return UnwrapJToken(vp.value.value);
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
                    if (!ctx.client.TryGetValue(ownership, rp.valueId, out AttributeValue? row))
                    {
                        throw new NSGetterRuntimeError(
                            $"Missing value reference: {rp.valueId}");
                    }
                    return UnwrapCached(row, ctx, ownership);
                }
                case KeyOfPointer kop:
                    return EvalKeyOf(kop.keyOf, scope, ctx, kop.optional == true);
                case OperationPointer op:
                    return EvalOperation(op.operation, scope, ctx);
                case FunctionPointer fp:
                    return EvalFunction(fp.function, scope, ctx);
                case ListLiteralPointer llp:
                {
                    var arr = new object?[llp.entries.Length];
                    for (int i = 0; i < llp.entries.Length; i++)
                    {
                        arr[i] = EvalPointer(llp.entries[i], scope, ctx);
                    }
                    return arr;
                }
                case DictLiteralPointer dlp:
                {
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
                    var innerThis = EvalPointer(cgp.thisPointer, scope, ctx);
                    if (cgp.optional == true && innerThis is null) return null;
                    // Try runtime dispatch via the receiver's typeId merged
                    // schema first — same trick the TS evaluator uses to
                    // honor runtime overrides regardless of the static
                    // compile-time binding.
                    var placement = CustomTypeInheritance.FindSchemaPlacement(
                        cgp.attributeId, EnumerateTypes(ctx.client));
                    if (placement is not null)
                    {
                        var dispatched = DispatchSchemaMember(innerThis, placement.schemaKey, ctx);
                        if (dispatched.kind == DispatchKind.Ok) return dispatched.value;
                    }
                    return DispatchNSGetterById(cgp.attributeId, innerThis, ctx);
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
                    return FormatForInterp(v, sp.sourceType, ctx);
                }
                case CallNativeFunctionPointer cnfp:
                    return EvalNativeFunctionCall(cnfp, scope, ctx);
                case NativeFunctionErrorCheckPointer nfec:
                    return EvalNativeFunctionErrorCheck(nfec, scope, ctx);
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
                    return $"callGetter {cgp.attributeId}";
                case KeyOfPointer kop:
                    return $"keyOf {EvalPointerKeyLabel(kop)}";
                case VariablePointer vp:
                    return $"variable {vp.variableId}";
                case ReferencePointer rp:
                    return $"reference {rp.valueId}";
                case CallNativeFunctionPointer cnfp:
                    return $"nativeCall {cnfp.attributeId}";
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

        private static object? EvalNativeFunctionCall(
            CallNativeFunctionPointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            if (ctx.nativeFunctionCallHandler is not null)
            {
                return ctx.nativeFunctionCallHandler(pointer, scope, ctx);
            }
            var receiver = EvalPointer(pointer.thisPointer, scope, ctx);
            if (pointer.optional == true && receiver is null)
            {
                return null;
            }
            var args = new object?[pointer.args.Length];
            for (int i = 0; i < pointer.args.Length; i++)
            {
                args[i] = EvalPointer(pointer.args[i], scope, ctx);
            }
            return ctx.client.InvokeNativeFunction(pointer.attributeId, receiver, args);
        }

        private static bool EvalNativeFunctionErrorCheck(
            NativeFunctionErrorCheckPointer pointer,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            try
            {
                EvalNativeFunctionCall(pointer.call, scope, ctx);
                return pointer.mode == NativeFunctionErrorCheckKind.DoesNotThrow;
            }
            catch (NeoDeferredFunctionRuntimeError)
            {
                throw;
            }
            catch
            {
                return pointer.mode == NativeFunctionErrorCheckKind.Throws;
            }
        }

        // ---------------------------------------------------------------
        // KeyOf — schema-key dispatch with runtime-typeId override hook
        // ---------------------------------------------------------------

        private static object? EvalKeyOf(
            KeyOf keyOf,
            Dictionary<string, object?> scope,
            Context ctx,
            bool optional)
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

            // List indexing: receiver is object?[]. Key is an int (or a
            // numeric string).
            if (receiver is object?[] arr)
            {
                int idx = ToIntKey(key);
                if (idx < 0 || idx >= arr.Length)
                {
                    throw new NSGetterRuntimeError(
                        $"List index out of bounds: {key}");
                }
                return ResolveValueIfId(arr[idx], ctx, FindRowOwnershipByReference(receiver, ctx));
            }

            string k = key?.ToString() ?? "null";
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
                throw new NSGetterRuntimeError("Custom value has no backing row id.");
            }

            // Dict / Custom record: receiver is Dictionary<string, ...>.
            if (TryAsObjectRecord(receiver, out IDictionary<string, object?>? record))
            {
                // Schema-dispatch if the receiver is a tracked Custom row.
                var dispatched = DispatchSchemaMember(receiver, k, ctx);
                if (dispatched.kind == DispatchKind.Ok) return dispatched.value;
                if (record!.TryGetValue(k, out var at))
                {
                    return ResolveValueIfId(at, ctx, FindRowOwnershipByReference(receiver, ctx));
                }
                throw new NSGetterRuntimeError($"Missing key '{k}' on object");
            }

            throw new NSGetterRuntimeError(
                $"Cannot index into {ReceiverTypeName(receiver)} with key '{key}'");
        }

        private enum DispatchKind { Ok, NoInfo }
        private readonly struct DispatchResult
        {
            public DispatchKind kind { get; }
            public object? value { get; }
            public DispatchResult(DispatchKind kind, object? value)
            {
                this.kind = kind;
                this.value = value;
            }
            public static DispatchResult Ok(object? v) => new(DispatchKind.Ok, v);
            public static DispatchResult NoInfo() => new(DispatchKind.NoInfo, null);
        }

        /// <summary>
        /// Runtime member-access dispatch on a Custom record. Mirrors
        /// the TS-side <c>dispatchSchemaMember</c>. Recovers the
        /// receiver's runtime <c>typeId</c> by reference-equality
        /// against tracked rows, walks the merged schema for that
        /// type, and dispatches to either an NSGetter (if the merged
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
            string? runtimeTypeId = FindRowTypeIdByReference(receiver, ctx);
            if (string.IsNullOrEmpty(runtimeTypeId)) return DispatchResult.NoInfo();

            IList<CustomType> chain;
            try
            {
                chain = CustomTypeInheritance.ResolveChain(
                    runtimeTypeId!,
                    id => ctx.client.TryGetType(id, out var t) ? t : null);
            }
            catch (CircularInheritanceError)
            {
                return DispatchResult.NoInfo();
            }
            var merged = CustomTypeInheritance.MergeSchemas(chain);
            MergedSchemaEntry? entry = null;
            foreach (var e in merged)
            {
                if (e.schemaKey == schemaKey) { entry = e; break; }
            }
            if (entry is null) return DispatchResult.NoInfo();

            if (!ctx.client.TryGetAttribute(entry.attributeId, out JsonAttribute? attr))
            {
                return DispatchResult.NoInfo();
            }

            if (attr.type == AttributeType.NSGetter)
            {
                if (ResolveCompiledGetter(entry.attributeId, ctx.client) is null)
                {
                    return DispatchResult.NoInfo();
                }
                return DispatchResult.Ok(DispatchNSGetterById(entry.attributeId, receiver, ctx));
            }

            if (record!.TryGetValue(schemaKey, out var at))
            {
                return DispatchResult.Ok(
                    ResolveValueIfId(at, ctx, FindRowOwnershipByReference(receiver, ctx), attr));
            }
            return DispatchResult.NoInfo();
        }

        /// <summary>
        /// Cycle-checked recursive evaluation of an NSGetter attribute by
        /// id. Walks <c>extendsAttributeId</c> for the first compiled
        /// <c>getter</c>, then runs it with the receiver as
        /// <c>__this__</c>.
        /// </summary>
        private static object? DispatchNSGetterById(
            string attributeId,
            object? receiver,
            Context ctx)
        {
            if (ctx.getterCallStack.Contains(attributeId))
            {
                throw new NSGetterRuntimeError(
                    $"Circular getter call: attribute '{attributeId}' is already being evaluated");
            }
            var getter = ResolveCompiledGetter(attributeId, ctx.client);
            if (getter is null)
            {
                string name = ctx.client.TryGetAttribute(attributeId, out JsonAttribute? attr)
                    ? attr.name
                    : attributeId;
                throw new NSGetterRuntimeError(
                    $"Getter '{name}' has no compiled `getter` — save its code to compile it");
            }
            var inner = ctx.WithGetterPushed(attributeId).WithThis(receiver);
            return Evaluate(getter, inner);
        }

        private static FunctionWithReturnType? ResolveCompiledGetter(
            string attrId, NeoClient client)
        {
            return CustomTypeInheritance.WalkExtendsAttributeChain(
                attrId,
                id => client.TryGetAttribute(id, out JsonAttribute? a) ? a : null,
                a => a is NSGetterAttribute ng ? ng.getter : null,
                requireType: AttributeType.NSGetter);
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
                    return ApplyArithmetic(info.type, operands);
                }
                case BooleanOperation boolOp:
                    return EvalBooleanExpression(boolOp.expression, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown operation kind {operation.GetType().Name}");
            }
        }

        private static object? ApplyArithmetic(string op, object?[] operands)
        {
            if (operands.Length == 0)
            {
                throw new NSGetterRuntimeError("Arithmetic operation with no operands");
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
                    return sb.ToString();
                }
                bool anyString = false;
                foreach (var o in operands) { if (o is string) { anyString = true; break; } }
                if (anyString)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var o in operands) sb.Append(StringifyForInterp(o));
                    return sb.ToString();
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

        private static object? EvalFunction(
            Function fn,
            Dictionary<string, object?> scope,
            Context ctx)
        {
            switch (fn)
            {
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
                        var result = EvalInstructions(inner.instructions, innerScope, ctx);
                        if (result.kind == InstructionResultKind.Return && result.value is bool b && b)
                        {
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
                        var result = EvalInstructions(inner.instructions, innerScope, ctx);
                        if (result.kind == InstructionResultKind.Return && result.value is bool b && b)
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
                        var result = EvalInstructions(inner.instructions, innerScope, ctx);
                        if (result.kind == InstructionResultKind.Return)
                        {
                            acc.Add(result.value);
                        }
                    });
                    return acc.ToArray();
                }
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown function kind {fn.GetType().Name}");
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

        private static IEnumerable<object?> CollectionEntries(object? c, Context ctx)
        {
            if (c is object?[] arr)
            {
                var ownership = FindRowOwnershipByReference(c, ctx);
                foreach (var e in arr) yield return ResolveValueIfId(e, ctx, ownership);
                yield break;
            }
            if (c is IDictionary<string, object?> dict)
            {
                var ownership = FindRowOwnershipByReference(c, ctx);
                foreach (var v in dict.Values) yield return ResolveValueIfId(v, ctx, ownership);
            }
        }

        private static void IterateCollection(
            object? c,
            Context ctx,
            Action<object? /*entry*/, object /*key*/, string? /*valueId*/> callback)
        {
            if (c is object?[] arr)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    var raw = arr[i];
                    var entry = ResolveValueIfId(raw, ctx, FindRowOwnershipByReference(c, ctx));
                    callback(entry, i, raw is string s ? s : null);
                }
                return;
            }
            if (c is IDictionary<string, object?> dict)
            {
                foreach (var kvp in dict)
                {
                    var entry = ResolveValueIfId(kvp.Value, ctx, FindRowOwnershipByReference(c, ctx));
                    callback(entry, kvp.Key, kvp.Value is string s ? s : null);
                }
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
            if (checkType.type == AttributeType.Null) return value is null;
            if (value is null) return false;
            switch (checkType.type)
            {
                case AttributeType.Bool: return value is bool;
                case AttributeType.Int:
                    return TryAsDouble(value, out double di) && di == System.Math.Truncate(di);
                case AttributeType.Float: return TryAsDouble(value, out _);
                case AttributeType.String: return value is string;
                case AttributeType.Sprite:
                    return value is IDictionary<string, object?> sprite &&
                        sprite.TryGetValue("fileId", out var spriteFileId) &&
                        spriteFileId is string &&
                        sprite.TryGetValue("sliceIndex", out var sliceIndex) &&
                        TryAsDouble(sliceIndex, out double slice) &&
                        slice == System.Math.Truncate(slice);
                case AttributeType.Audio:
                    return value is IDictionary<string, object?> audio &&
                        audio.TryGetValue("fileId", out var audioFileId) &&
                        audioFileId is string;
                case AttributeType.List: return value is object?[];
                case AttributeType.Dictionary:
                    return value is IDictionary<string, object?>;
                case AttributeType.Enum:
                {
                    if (value is not object?[] arr) return false;
                    foreach (var e in arr) if (e is not string) return false;
                    return true;
                }
                case AttributeType.Custom:
                {
                    if (value is not IDictionary<string, object?>) return false;
                    string? runtimeTypeId = FindRowTypeIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(runtimeTypeId)) return false;
                    string checkTypeId = (checkType as CustomTypeInfo)?.typeId ?? "";
                    if (runtimeTypeId == checkTypeId) return true;
                    try
                    {
                        var chain = CustomTypeInheritance.ResolveChain(
                            runtimeTypeId!,
                            id => ctx.client.TryGetType(id, out var t) ? t : null);
                        foreach (var t in chain) if (t.id == checkTypeId) return true;
                    }
                    catch (CircularInheritanceError)
                    {
                        return false;
                    }
                    return false;
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
            JsonAttribute? attribute = null)
        {
            if (at is not string id) return at;
            var ownership = preferredOwnership ?? ResolveOwnershipForValueId(ctx, id);
            if (!ctx.client.TryGetValue(ownership, id, out AttributeValue? row)) return at;
            var v = UnwrapCached(row, ctx, ownership, attribute);
            if (v is object?[] arr && arr.Length == 1 && arr[0] is string singleId)
            {
                var singleOwnership = ResolveOwnershipForValueId(ctx, singleId);
                if (ctx.client.TryGetValue(singleOwnership, singleId, out AttributeValue? next))
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
                        out AttributeValue? row))
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

        // ---------------------------------------------------------------
        // Wire-value bridging — turns AttributeValue subclasses into the
        // plain CLR shapes the evaluator manipulates (object?[],
        // IDictionary, primitives, etc.).
        // ---------------------------------------------------------------

        private static object? ExtractWireValue(
            AttributeValue row,
            NeoValueOwnership ownership,
            JsonAttribute? attribute,
            Context ctx)
        {
            return row switch
            {
                BoolAttributeValue b => b.value,
                NumberAttributeValue n => n.value,
                StringAttributeValue s => attribute is StringAttribute stringAttribute
                    ? ResolveStringValue(s, stringAttribute, ctx)
                    : s.value,
                ArrayAttributeValue a => a.value is null
                    ? null
                    : ToObjectArray(a.value),
                ObjectAttributeValue o => o.value is null
                    ? null
                    : ToObjectDict(row.id, ownership, o.value),
                FileAttributeValue f => f.value is null
                    ? null
                    : new Dictionary<string, object?> { ["fileId"] = f.value.fileId },
                SpriteAttributeValue sp => sp.value is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["fileId"] = sp.value.fileId,
                        ["sliceIndex"] = sp.value.sliceIndex,
                    },
                NullAttributeValue _ => null,
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
        /// <see cref="FindRowTypeIdByReference"/> can recover the
        /// source row from the unwrapped value. Skipped for
        /// primitives because boxed-primitive reference equality
        /// would false-positive across rows that share a value.</para>
        /// </summary>
        private static object? UnwrapCached(
            AttributeValue row,
            Context ctx,
            NeoValueOwnership ownership,
            JsonAttribute? attribute = null)
        {
            string cacheKey = RowCacheKey(ownership, row.id, attribute);
            if (ctx.rowUnwrapCache.TryGetValue(cacheKey, out var cached)) return cached;
            var unwrapped = ExtractWireValue(row, ownership, attribute, ctx);
            ctx.rowUnwrapCache[cacheKey] = unwrapped;
            // Reverse-index only object-shaped unwraps. Primitive
            // boxes don't have meaningful reference identity for our
            // lookups (two rows with `value = "hi"` would share a
            // boxed string; two rows with `value = 5` would share a
            // boxed double after JIT folding). The TS reference-
            // equality lookup only ever fires for record / array
            // values where this is a non-issue.
            if (unwrapped is IDictionary<string, object?> || unwrapped is object?[])
            {
                ctx.rowReverseIndex[unwrapped!] = new RowReference(row.id, ownership);
            }
            return unwrapped;
        }

        private static string RowCacheKey(
            NeoValueOwnership ownership,
            string rowId,
            JsonAttribute? attribute = null) =>
            ownership.ToString() + ":" + rowId + ":" + (attribute?.id ?? "");

        private static string? ResolveStringValue(
            StringAttributeValue value,
            StringAttribute attribute,
            Context ctx)
        {
            if (value.value == null) return null;
            if (!attribute.localizable) return value.value;
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

        private static string? FindRowTypeIdByReference(object? value, Context ctx)
        {
            // O(1) reverse lookup against the per-context unwrap cache.
            // Only object-shaped values are indexed (see UnwrapCached);
            // primitives correctly miss because reference identity isn't
            // meaningful for them.
            if (!TryFindRowReferenceByReference(value, ctx, out RowReference rowRef)) return null;
            if (!ctx.client.TryGetValue(rowRef.ownership, rowRef.valueId, out AttributeValue? row))
            {
                return null;
            }
            if (!string.IsNullOrEmpty(row.typeId)) return row.typeId;
            return ctx.client.TryInferAttributeForValueId(rowRef.valueId, out JsonAttribute? attribute)
                && attribute is CustomAttribute customAttribute
                    ? customAttribute.customTypeId
                    : null;
        }

        // ---------------------------------------------------------------
        // Project enumeration helpers — wrap the NeoClient's keyed-by-id
        // dicts behind enumerable accessors so the evaluator (and helpers
        // like FindSchemaPlacement) can iterate.
        // ---------------------------------------------------------------

        private static IEnumerable<CustomType> EnumerateTypes(NeoClient client)
        {
            // The client doesn't expose its types map directly — the only
            // enumeration path is to fetch by id. To support
            // FindSchemaPlacement we need the full list. Walk via the
            // attributes the client knows about and collect the unique
            // typeIds they reference. Acceptable for the evaluator's
            // purpose: every type referenced by a Custom attribute will
            // appear, which is exactly the set FindSchemaPlacement needs.
            var seen = new HashSet<string>();
            foreach (var pair in EnumerateAllAttributes(client))
            {
                if (pair.Value is CustomAttribute ca)
                {
                    if (seen.Add(ca.customTypeId)
                        && client.TryGetType(ca.customTypeId, out CustomType? t))
                    {
                        yield return t;
                    }
                }
            }
        }

        // Both EnumerateAllAttributes and EnumerateAllValues need access
        // to the client's underlying maps. NeoClient currently doesn't
        // expose them as IEnumerable, so we'd need a small accessor
        // there. For the first cut, route through the public
        // ProjectData / ProjectSaveData since the evaluator itself
        // doesn't need the full set most of the time — just the
        // FindSchemaPlacement and FindRowTypeIdByReference paths.
        //
        // Simplest fix: expose IReadOnlyDictionary-typed views on
        // NeoClient. Done in NeoClient updates below — see
        // `NeoClient.attributes` / `NeoClient.values` / `NeoClient.types` /
        // `NeoClient.enums`.

        private static IEnumerable<KeyValuePair<string, JsonAttribute>> EnumerateAllAttributes(NeoClient client)
        {
            foreach (var kvp in client.attributes) yield return kvp;
        }

        private static IEnumerable<KeyValuePair<string, AttributeValue>> EnumerateAllValues(NeoClient client)
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
                case AttributeType.Enum:
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
                case AttributeType.Custom:
                {
                    string typeId = (sourceType as CustomTypeInfo)?.typeId ?? "";
                    string typeName = ctx.client.TryGetType(typeId, out CustomType? ct)
                        ? ct.name
                        : typeId;
                    string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                    return $"(Custom<{typeName}>, Value<{rowId}>)";
                }
                case AttributeType.List:
                {
                    var entryType = (sourceType as CollectionTypeInfo)?.entryTypeInfo;
                    string entryName = entryType is null ? "unknown" : DescribeRuntimeType(entryType, ctx);
                    string rowId = FindRowIdByReference(value, ctx) ?? "<unknown>";
                    return $"(List<{entryName}>, Value<{rowId}>)";
                }
                case AttributeType.Dictionary:
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
                case AttributeType.Null: return "null";
                case AttributeType.Bool: return "bool";
                case AttributeType.Int: return "int";
                case AttributeType.Float: return "float";
                case AttributeType.String: return "string";
                case AttributeType.Sprite: return "SpriteInfo";
                case AttributeType.Audio: return "AudioClipInfo";
                case AttributeType.Custom:
                {
                    string typeId = (t as CustomTypeInfo)?.typeId ?? "";
                    return ctx.client.TryGetType(typeId, out CustomType? ct) ? ct.name : typeId;
                }
                case AttributeType.Enum:
                {
                    string enumId = (t as EnumTypeInfo)?.enumId ?? "";
                    return ctx.client.TryGetEnum(enumId, out JsonEnum? je) ? je.name : enumId;
                }
                case AttributeType.List:
                {
                    var inner = (t as CollectionTypeInfo)?.entryTypeInfo;
                    return inner is null ? "List<unknown>" : $"List<{DescribeRuntimeType(inner, ctx)}>";
                }
                case AttributeType.Dictionary:
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

        private static NeoValueOwnership? FindRowOwnershipByReference(object? value, Context ctx)
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
