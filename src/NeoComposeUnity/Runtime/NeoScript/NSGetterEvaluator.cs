// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            public NeoClient client { get; }
            public object? thisValue { get; }
            public object? rootValue { get; }
            /// <summary>
            /// Stack of NSGetter attribute ids currently in-flight. Threaded
            /// through callGetter recursion via fresh-copy children so a
            /// cycle (`A.x` calls `B.y` calls `A.x` on a different receiver)
            /// trips before the runtime stack overflows.
            /// </summary>
            public IReadOnlyCollection<string> getterCallStack { get; }

            public Context(
                NeoClient client,
                object? thisValue,
                object? rootValue,
                IReadOnlyCollection<string>? getterCallStack = null)
            {
                this.client = client;
                this.thisValue = thisValue;
                this.rootValue = rootValue;
                this.getterCallStack = getterCallStack ?? System.Array.Empty<string>();
            }

            internal Context WithGetterPushed(string attributeId)
            {
                var next = new HashSet<string>(getterCallStack) { attributeId };
                return new Context(client, thisValue, rootValue, next);
            }

            internal Context WithThis(object? newThisValue)
            {
                return new Context(client, newThisValue, rootValue, getterCallStack);
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
            };
            var result = EvalInstructions(getter.instructions, scope, ctx);
            if (result.kind == InstructionResultKind.Return) return result.value;
            throw new NSGetterRuntimeError("Function ended without a return statement");
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
                    if (!ctx.client.TryGetValue(rp.valueId, out AttributeValue? row))
                    {
                        throw new NSGetterRuntimeError(
                            $"Missing value reference: {rp.valueId}");
                    }
                    return ExtractWireValue(row);
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
                            "Unexpectedly found null while force-unwrapping a value");
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
                default:
                    throw new NSGetterRuntimeError(
                        $"Unknown pointer kind {pointer.GetType().Name}");
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
                return ResolveValueIfId(arr[idx], ctx);
            }

            // Dict / Custom record: receiver is Dictionary<string, ...>.
            if (TryAsObjectRecord(receiver, out IDictionary<string, object?>? record))
            {
                string k = key?.ToString() ?? "null";
                // Schema-dispatch if the receiver is a tracked Custom row.
                var dispatched = DispatchSchemaMember(receiver, k, ctx);
                if (dispatched.kind == DispatchKind.Ok) return dispatched.value;
                if (record!.TryGetValue(k, out var at))
                {
                    return ResolveValueIfId(at, ctx);
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
                return DispatchResult.Ok(ResolveValueIfId(at, ctx));
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
                foreach (var e in arr) yield return ResolveValueIfId(e, ctx);
                yield break;
            }
            if (c is IDictionary<string, object?> dict)
            {
                foreach (var v in dict.Values) yield return ResolveValueIfId(v, ctx);
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
                    var entry = ResolveValueIfId(raw, ctx);
                    callback(entry, i, raw is string s ? s : null);
                }
                return;
            }
            if (c is IDictionary<string, object?> dict)
            {
                foreach (var kvp in dict)
                {
                    var entry = ResolveValueIfId(kvp.Value, ctx);
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
        /// </summary>
        private static object? ResolveValueIfId(object? at, Context ctx)
        {
            if (at is not string id) return at;
            if (!ctx.client.TryGetValue(id, out AttributeValue? row)) return at;
            var v = ExtractWireValue(row);
            if (v is object?[] arr && arr.Length == 1 && arr[0] is string singleId)
            {
                if (ctx.client.TryGetValue(singleId, out AttributeValue? next))
                {
                    return ExtractWireValue(next);
                }
            }
            return v;
        }

        // ---------------------------------------------------------------
        // Wire-value bridging — turns AttributeValue subclasses into the
        // plain CLR shapes the evaluator manipulates (object?[],
        // IDictionary, primitives, etc.).
        // ---------------------------------------------------------------

        private static object? ExtractWireValue(AttributeValue row)
        {
            return row switch
            {
                BoolAttributeValue b => b.value,
                NumberAttributeValue n => n.value,
                StringAttributeValue s => s.value,
                ArrayAttributeValue a => a.value is null
                    ? null
                    : ToObjectArray(a.value),
                ObjectAttributeValue o => o.value is null
                    ? null
                    : ToObjectDict(o.value),
                NullAttributeValue _ => null,
                _ => null,
            };
        }

        private static object?[] ToObjectArray(string[] arr)
        {
            var result = new object?[arr.Length];
            for (int i = 0; i < arr.Length; i++) result[i] = arr[i];
            return result;
        }

        private static IDictionary<string, object?> ToObjectDict(IDictionary<string, string> dict)
        {
            var result = new Dictionary<string, object?>(dict.Count);
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
            // Iterate every value the client knows about (saveData first
            // wins, then data). Reference equality: the unwrapped value
            // we hold should be the same dict instance the row exposes.
            // Note that we unwrap each row's value lazily via ExtractWireValue,
            // which builds new objects each call — so reference equality
            // here only works against rows that were the source of the
            // value via a prior ExtractWireValue + memoized unwrap. In
            // practice the evaluator's KeyOf chain reads
            // resolveValueIfId(stringId) -> ExtractWireValue(row) -> the
            // returned object. Two calls produce two different objects.
            //
            // To make the reference-lookup work the same as TS (where
            // row.value IS the heap object), we'd need to cache unwrapped
            // values per row. Defer that optimization — for now scan all
            // rows by unwrapping each and checking reference equality.
            // This works correctly for runtime override dispatch in
            // most cases because the receiver was just freshly unwrapped
            // from the row in the immediately-previous evaluator hop.
            //
            // TODO: cache UnwrappedValueRow → object? per Context to
            // make this O(1) and reference-stable.
            foreach (var pair in EnumerateAllValues(ctx.client))
            {
                if (ReferenceEquals(ExtractWireValue(pair.Value), value))
                {
                    return pair.Value.typeId;
                }
            }
            return null;
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
                        labels.Add(jsonEnum.options.TryGetValue(id, out EnumOption opt)
                            ? opt.text
                            : id);
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

        private static string? FindRowIdByReference(object? value, Context ctx)
        {
            foreach (var pair in EnumerateAllValues(ctx.client))
            {
                if (ReferenceEquals(ExtractWireValue(pair.Value), value)) return pair.Value.id;
            }
            return null;
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
