// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    internal static class NeoSequenceContract
    {
        internal const string Enumerable = "system_d1413390-a2a6-4137-a8f8-e931da847345";
        internal const string Enumerator = "system_1604985c-2d92-4aca-9fcb-16a667aaaf1a";
        internal const string EnumerableElement = "system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2";
        internal const string EnumeratorElement = "system_8c8392fe-41f0-4116-9ac8-e325a187d8aa";
        internal const string GetEnumerator = "system_a14c079d-7999-4125-81e0-8cc7226dbcd6";
        internal const string Current = "system_a4ee2931-f6c6-41cb-8ee9-4fc1b12d97ff";
        internal const string MoveNext = "system_f0c37057-082c-4a23-8c9f-27ea9a17b5be";
        internal const string Dispose = "system_9b9cd079-e20d-47e5-8d86-11080b867fe8";
        internal static readonly TypeInfo Int = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
    }

    public static partial class NSGetterEvaluator
    {
        internal sealed class SequenceOwners
        {
            private readonly HashSet<NeoClient.SequenceGeneration> owners = new();
            private readonly HashSet<NeoClient.SequenceGeneration> distinct = new();
            private readonly List<NeoClient.SequenceGeneration> merged = new();

            internal void Attach(NeoClient.SequenceGeneration group)
            {
                if (group.Live)
                    owners.Add(group);
            }

            internal void Inherit(SequenceOwners? source)
            {
                if (source is null)
                    return;
                foreach (var group in source.owners)
                    Attach(group);
            }

            internal IReadOnlyList<NeoClient.SequenceGeneration>? Enter(NeoClient client)
            {
                var previous = client.ActiveSequenceOwners;
                distinct.Clear();
                merged.Clear();
                if (previous is not null)
                    for (int i = 0; i < previous.Count; i++)
                        if (previous[i].Live && distinct.Add(previous[i]))
                            merged.Add(previous[i]);
                bool added = false;
                foreach (var owner in owners)
                    if (owner.Live && distinct.Add(owner))
                    {
                        added = true;
                        merged.Add(owner);
                    }
                if (added)
                    client.ActiveSequenceOwners = merged;
                return previous;
            }
        }

        internal static void AttachSequenceOwner(object? value, NeoClient.SequenceGeneration? owner)
        {
            if (owner is null)
                return;
            if (value is GeneratorSequence or RangeSequence && value is ScriptSequence sequence)
                (sequence.Dependencies ??= new()).Attach(owner);
            else if (value is GeneratorCursor cursor)
                (cursor.Dependencies ??= new()).Attach(owner);
        }

        private static object? DispatchSequenceGetter(string memberId, object? receiver, FunctionWithReturnType getter,
            RowReference? receiverRef, Context ctx, TypeInfo[]? typeArguments, bool receiverGeneric)
        {
            NeoClient client = ctx.client;
            receiverRef ??= FindRowReference(receiver, ctx);
            bool watched = !receiverGeneric && receiverRef is not null && client.WatchesGetters(receiverRef.valueId);
            int frame = ctx.EnterGetter(memberId, receiver);
            if (typeArguments is not null)
                ctx.BindFunctionTypeArguments(typeArguments);
            if (!watched)
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
            var enclosing = client.BeginGetterReadCapture();
            object? result = null;
            try
            {
                result = Evaluate(getter, ctx, Array.Empty<object?>(), frame);
                return result;
            }
            finally
            {
                ctx.ExitFunction(frame);
                var capture = client.EndGetterReadCapture(enclosing);
                var owner = client.MemoizeSequenceReads(GetterMemoKeyOf(receiverRef!, memberId, ctx), capture);
                AttachSequenceOwner(result, owner);
            }
        }

        internal abstract class ScriptSequence
        {
            internal readonly NeoClient Owner;
            internal readonly TypeInfo ElementType;
            internal SequenceOwners? Dependencies;

            protected ScriptSequence(NeoClient owner, TypeInfo elementType)
            {
                Owner = owner;
                ElementType = elementType;
            }

            internal ScriptCursor GetEnumerator(Context ctx)
            {
                RequireSequenceClient(Owner, ctx);
                var previous = ctx.client.ActiveSequenceOwners;
                Dependencies?.Enter(ctx.client);
                try
                {
                    ScriptCursor cursor = CreateCursor(ctx);
                    if (Dependencies is not null)
                        (cursor.Dependencies ??= new()).Inherit(Dependencies);
                    return cursor;
                }
                finally
                {
                    ctx.client.ActiveSequenceOwners = previous;
                }
            }

            protected abstract ScriptCursor CreateCursor(Context ctx);
        }

        internal abstract class ScriptCursor
        {
            private enum CursorState
            {
                NotStarted, Running, Yielded, Completed, Disposed
            }
            private CursorState state;
            private object? current;
            internal readonly NeoClient Owner;
            internal readonly TypeInfo ElementType;
            internal SequenceOwners? Dependencies;

            protected ScriptCursor(NeoClient owner, TypeInfo elementType)
            {
                Owner = owner;
                ElementType = elementType;
            }

            internal bool MoveNext(Context ctx)
            {
                RequireSequenceClient(Owner, ctx);
                if (state == CursorState.Running)
                    throw new NSGetterRuntimeError("Cannot advance a cursor while it is already running.");
                if (state is CursorState.Completed or CursorState.Disposed)
                    return false;
                state = CursorState.Running;
                var previous = ctx.client.ActiveSequenceOwners;
                Dependencies?.Enter(ctx.client);
                try
                {
                    if (Advance(ctx, out current))
                    {
                        state = CursorState.Yielded;
                        return true;
                    }
                    state = CursorState.Completed;
                    current = null;
                    Release();
                    Dependencies = null;
                    return false;
                }
                catch
                {
                    state = CursorState.Completed;
                    current = null;
                    Release();
                    throw;
                }
                finally
                {
                    ctx.client.ActiveSequenceOwners = previous;
                }
            }

            internal object? Current(Context ctx)
            {
                RequireSequenceClient(Owner, ctx);
                if (state != CursorState.Yielded && !(ExternalPosition && state == CursorState.NotStarted))
                    throw new NSGetterRuntimeError("Current requires a cursor positioned on a yielded value.");
                var previous = ctx.client.ActiveSequenceOwners;
                Dependencies?.Enter(ctx.client);
                try
                {
                    return ReadCurrent(ctx);
                }
                finally
                {
                    ctx.client.ActiveSequenceOwners = previous;
                }
            }

            protected virtual bool ExternalPosition => false;
            protected virtual object? ReadCurrent(Context ctx) => current;

            internal void Dispose(Context ctx)
            {
                RequireSequenceClient(Owner, ctx);
                if (state == CursorState.Running)
                    throw new NSGetterRuntimeError("Cannot dispose a cursor while it is running.");
                if (state == CursorState.Disposed)
                    return;
                var previous = ctx.client.ActiveSequenceOwners;
                Dependencies?.Enter(ctx.client);
                state = CursorState.Disposed;
                current = null;
                try
                {
                    Close(ctx);
                }
                finally
                {
                    ctx.client.ActiveSequenceOwners = previous;
                    Release();
                    Dependencies = null;
                }
            }

            internal void Abandon()
            {
                state = CursorState.Disposed;
                current = null;
                Release();
                Dependencies = null;
            }

            protected abstract bool Advance(Context ctx, out object? value);
            protected virtual void Close(Context ctx)
            {
            }
            protected virtual void Release()
            {
            }
        }

        internal static object CreateGenerator(FunctionWithReturnType body, NeoScriptScope scope, Context ctx)
        {
            var capture = new GeneratorCapture(body, scope.Materialize(), ctx.thisValue, ctx.rootValue,
                ctx.functionTypeArguments is { } arguments ? (TypeInfo[])arguments.Clone() : null,
                CloseContextType(body.generator!.elementType, ctx));
            ctx.allocationTracker.MarkEscaped(capture.Bindings, ctx);
            return body.generator.kind == "enumerable"
                ? new GeneratorSequence(ctx.client, capture)
                : new GeneratorCursor(ctx.client, capture);
        }

        private sealed class GeneratorCapture
        {
            internal readonly FunctionWithReturnType Body;
            internal readonly Dictionary<string, object?> Bindings;
            internal readonly object? Receiver;
            internal readonly object? Root;
            internal readonly bool DynamicRoot;
            internal readonly TypeInfo[]? TypeArguments;
            internal readonly TypeInfo ElementType;
            internal GeneratorCapture(FunctionWithReturnType body, Dictionary<string, object?> bindings,
                object? receiver, object? root, TypeInfo[]? typeArguments, TypeInfo elementType)
            {
                Body = body;
                Bindings = bindings;
                // Dialogue context is obtained from the caller on each operation.
                Bindings.Remove("__context__");
                Bindings.Remove("__root__");
                Receiver = receiver;
                DynamicRoot = NeoScriptValueMarshaller.IsContextRoot(root);
                Root = DynamicRoot ? null : root;
                TypeArguments = typeArguments;
                ElementType = elementType;
            }
        }

        private sealed class GeneratorSequence : ScriptSequence
        {
            private readonly GeneratorCapture capture;
            internal GeneratorSequence(NeoClient owner, GeneratorCapture capture) : base(owner, capture.ElementType)
            {
                this.capture = capture;
            }
            protected override ScriptCursor CreateCursor(Context ctx) => new GeneratorCursor(Owner, capture);
        }

        private sealed class GeneratorCursor : ScriptCursor
        {
            private GeneratorCapture? capture;
            private NeoScriptExecutor.GeneratorExecution? execution;
            private NeoScriptScope? scope;
            internal GeneratorCursor(NeoClient owner, GeneratorCapture capture) : base(owner, capture.ElementType)
            {
                this.capture = capture;
            }

            protected override bool Advance(Context ctx, out object? value)
            {
                if (scope is null)
                {
                    scope = new NeoScriptScope(capture!.Body.scopeLayout ??= new NeoScriptScopeLayout(capture.Body));
                    foreach (var binding in capture.Bindings)
                        scope.SetLocal(binding.Key, binding.Value);
                }
                execution ??= new NeoScriptExecutor.GeneratorExecution(capture!.Body, scope, ElementType);
                bool yielded = Operate(ctx, false);
                value = yielded ? execution.Current : null;
                return yielded;
            }

            protected override void Close(Context ctx)
            {
                if (execution is not null)
                    Operate(ctx, true);
            }

            private bool Operate(Context ctx, bool closing)
            {
                GeneratorCapture captured = capture!;
                object? callerRoot = ctx.rootValue;
                Context? previousContext = ctx.client.ActiveScriptContext;
                int frame = ctx.EnterThis(captured.Receiver);
                bool tracking = false, writing = false, tracing = false;
                var previousRetention = ctx.allocationTracker.ActiveGeneratorRetention;
                execution!.Retention.ParentActive = previousRetention;
                ctx.allocationTracker.ActiveGeneratorRetention = execution.Retention;
                try
                {
                    ctx.BindRoot(captured.DynamicRoot ? NeoScriptValueMarshaller.ResolveRoot(ctx.client, ctx) : captured.Root);
                    scope!["__root__"] = ctx.rootValue;
                    ctx.BindFunctionTypeArguments(captured.TypeArguments);
                    scope["__context__"] = ctx.contextValue;
                    NeoScriptExecutor.PrepareFunctionContext(ctx, NeoScriptExecutionOptions.ForImmediate(ctx.client), frame);
                    ctx.allocationTracker.EnterExecution();
                    tracking = true;
                    ctx.client.EnterScriptWrites();
                    writing = true;
                    ctx.client.ActiveScriptContext = ctx;
                    ctx.trace.Push(captured.Body);
                    tracing = true;
                    if (closing)
                        execution!.Close(ctx);
                    else if (execution!.Run(ctx).IsYielded)
                    {
                        execution.MarkRetained(ctx);
                        return true;
                    }
                    return false;
                }
                finally
                {
                    ctx.allocationTracker.ActiveGeneratorRetention = previousRetention;
                    execution!.Retention.ParentActive = null;
                    scope!.Remove("__context__");
                    scope.Remove("__root__");
                    if (tracing)
                        ctx.trace.Pop();
                    try
                    {
                        try
                        {
                            if (tracking)
                                ctx.allocationTracker.ExitExecution(ctx.client, ctx, default);
                        }
                        finally
                        {
                            if (writing)
                                ctx.client.ExitScriptWrites();
                        }
                    }
                    finally
                    {
                        ctx.client.ActiveScriptContext = previousContext;
                        ctx.BindRoot(callerRoot);
                        ctx.ExitFunction(frame);
                    }
                }
            }

            protected override void Release()
            {
                execution?.Abandon();
                execution = null;
                scope = null;
                capture = null;
            }
        }

        private sealed class RangeSequence : ScriptSequence
        {
            private readonly long start;
            private readonly long end;
            internal RangeSequence(NeoClient owner, long start, long end) : base(owner, NeoSequenceContract.Int)
            {
                this.start = start;
                this.end = end;
            }
            protected override ScriptCursor CreateCursor(Context ctx) => new RangeCursor(Owner, start, end);
        }

        private sealed class RangeCursor : ScriptCursor
        {
            private long next;
            private readonly long end;
            internal RangeCursor(NeoClient owner, long start, long end) : base(owner, NeoSequenceContract.Int)
            {
                next = start;
                this.end = end;
            }
            protected override bool Advance(Context ctx, out object? value)
            {
                if (next >= end)
                {
                    value = null;
                    return false;
                }
                value = (double)next++;
                return true;
            }
        }

        private sealed class ScriptCollectionCursor : ScriptCursor
        {
            private readonly CollectionSnapshot snapshot = new();
            private readonly LookupMember? slotLookup;
            private readonly TypeInfo? slotTarget;
            private int index;
            internal bool Values => snapshot.Values;
            internal object? Raw => snapshot.Raw(index - 1);
            internal ScriptCollectionCursor(object? collection, TypeInfo elementType, Context ctx) : base(ctx.client, elementType)
            {
                snapshot.Take(collection, ctx);
                // A query reads a Lookup of slot references as a direct query does.
                slotTarget = SlotSelection(FindRowReference(collection, ctx), ctx, out slotLookup);
            }
            protected override bool Advance(Context ctx, out object? value)
            {
                if (index >= snapshot.Count)
                {
                    value = null;
                    return false;
                }
                value = slotTarget is not null && snapshot.Raw(index) is string id
                    ? SlotReferenceOf(id, slotLookup!, slotTarget)
                    : snapshot.Resolve(index, ctx);
                index++;
                return true;
            }
            protected override void Release() => snapshot.Clear();
        }

        private static void RequireSequenceClient(NeoClient owner, Context ctx)
        {
            if (owner.IsDisposed)
                throw new ObjectDisposedException(nameof(NeoClient));
            if (!ReferenceEquals(owner, ctx.client))
                throw new NSGetterRuntimeError("A sequence belongs to a different project client.");
        }

        private static readonly CallFunctionPointer AcquireCall = SequenceCall("GetEnumerator");
        private static readonly CallFunctionPointer MoveCall = SequenceCall("MoveNext");
        private static readonly CallFunctionPointer DisposeCall = SequenceCall("Dispose");

        private static CallFunctionPointer SequenceCall(string key) => new()
        {
            type = PointerKind.CallFunction,
            memberKey = key,
            args = Array.Empty<Pointer>(),
            callSiteId = "$sequence:" + key,
            receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "$sequence" }),
        };

        private static object? InvokeSequenceMember(object? receiver, CallFunctionPointer call, Context ctx)
        {
            CallSiteTarget? target = ResolveCallTarget(call, receiver, ctx);
            if (target?.function is { } function)
                return NeoNSFunctionRuntime.InvokeImmediate(ctx.client, function, receiver, Array.Empty<object?>(), ctx);
            if (target?.native is not null)
                return InvokeNativeFunction(target.memberId, target.nativeFunction, receiver, Array.Empty<object?>(), ctx);
            throw new NSGetterRuntimeError($"Sequence member '{call.memberKey}' could not be resolved on its receiver.");
        }

        internal static object? AcquireSequenceCursor(object? value, TypeInfo elementType, Context ctx)
        {
            if (value is ScriptSequence sequence)
                return sequence.GetEnumerator(ctx);
            if (value is ScriptCursor)
                throw new NSGetterRuntimeError("An IEnumerator is not an IEnumerable. Advance it directly or collect it into a List.");
            if (FindRowClassIdByReference(value, ctx) is not null)
                return InvokeSequenceMember(value, AcquireCall, ctx);
            return new ScriptCollectionCursor(value, elementType, ctx);
        }

        internal static bool AdvanceSequenceCursor(object? value, Context ctx)
        {
            if (value is ScriptCursor cursor)
                return cursor.MoveNext(ctx);
            return InvokeSequenceMember(value, MoveCall, ctx) is bool result
                ? result : throw new NSGetterRuntimeError("MoveNext must return bool.");
        }

        internal static object? ReadSequenceCurrent(object? value, Context ctx)
        {
            if (value is ScriptCursor cursor)
                return cursor.Current(ctx);
            object? result = DispatchSchemaMember(value, "Current", ctx);
            return Dispatched(result) ? result : throw new NSGetterRuntimeError("The cursor has no Current member.");
        }

        internal static void DisposeSequenceCursor(object? value, Context ctx)
        {
            if (value is ScriptCursor cursor)
                cursor.Dispose(ctx);
            else
                InvokeSequenceMember(value, DisposeCall, ctx);
        }

        internal static T TagSequenceCollection<T>(T value, TypeInfo? type, Context ctx) where T : class
        {
            TypeInfo? element = type switch
            {
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                FunctionArgumentTypeInfo argument when argument.type is MemberKind.List
                    or MemberKind.Set or MemberKind.Dictionary or MemberKind.Lookup => argument.entryTypeInfo,
                _ => null,
            };
            return element is null ? value : TagSequenceCollectionElement(value, element, ctx);
        }

        internal static object SequenceCollectionIdentity(object value) =>
            value is object?[] array && runtimeLists.TryGetValue(array, out RuntimeListIdentity identity)
                ? identity.origin : value;

        private static T TagSequenceCollectionElement<T>(T value, TypeInfo element, Context ctx) where T : class
        {
            if (ctx.client.SequenceCollectionElements.TryGetValue(SequenceCollectionIdentity(value), out _))
                return value;
            if (value is object?[] { Length: 0 } empty && ReferenceEquals(empty, Array.Empty<object?>()))
                value = (T)(object)new object?[0];
            // Type metadata needs stable identity even for a fresh literal.
            // Only bindings/captures mark it shared with another holder.
            if (value is object?[] list)
                runtimeLists.GetValue(list, array => new RuntimeListIdentity(array));
            if (NeoNSFunctionRuntime.ContainsGeneric(element)
                && ctx.functionTypeArguments is null
                && (ctx.thisValue is null || NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, ctx.thisValue, ctx, "Collection element").Count == 0))
                return value;
            ctx.client.SequenceCollectionElements.Add(SequenceCollectionIdentity(value), CloseContextType(element, ctx));
            return value;
        }

        private static TypeInfo? SequenceCollectionElement(object? value, Context ctx)
        {
            if (value is null)
                return null;
            if (ctx.client.SequenceCollectionElements.TryGetValue(SequenceCollectionIdentity(value), out TypeInfo known))
                return known;
            if (CollectionEntryMember(value, ctx) is { } member)
            {
                TypeInfo element = NeoNSFunctionRuntime.TypeInfoFromBindingMember(ctx.client, member,
                    new Dictionary<string, NeoGenericEnvEntry>(), new HashSet<string>());
                ctx.client.SequenceCollectionElements.Add(SequenceCollectionIdentity(value), element);
                return element;
            }
            return null;
        }

        internal static bool IsIntrinsicSequence(object? value, TypeInfo type, Context ctx)
        {
            string? interfaceId = type switch
            {
                InterfaceTypeInfo protocol => protocol.interfaceId,
                FunctionArgumentTypeInfo argument when argument.type == MemberKind.Interface => argument.interfaceId,
                _ => null,
            };
            Dictionary<string, TypeInfo>? arguments = type switch
            {
                InterfaceTypeInfo protocol => protocol.typeArguments,
                FunctionArgumentTypeInfo argument => argument.typeArguments,
                _ => null,
            };
            string key;
            TypeInfo? element;
            if (interfaceId == NeoSequenceContract.Enumerable)
            {
                key = NeoSequenceContract.EnumerableElement;
                element = value is ScriptSequence sequence ? sequence.ElementType : SequenceCollectionElement(value, ctx);
            }
            else if (interfaceId == NeoSequenceContract.Enumerator)
            {
                key = NeoSequenceContract.EnumeratorElement;
                element = (value as ScriptCursor)?.ElementType;
            }
            else
                return false;
            return element is not null && arguments is { Count: 1 }
                && arguments.TryGetValue(key, out TypeInfo expected)
                && NeoClient.TypeInfoMatches(element, CloseContextType(expected, ctx));
        }

        internal static bool TrySequenceCall(CallFunctionPointer call, object? receiver, Context ctx, out object? value)
        {
            value = null;
            if (call.memberId == NeoSequenceContract.GetEnumerator && receiver is ScriptSequence sequence)
            {
                value = sequence.GetEnumerator(ctx);
                return true;
            }
            if (call.memberId == NeoSequenceContract.GetEnumerator && SequenceCollectionElement(receiver, ctx) is { } element)
            {
                value = AcquireSequenceCursor(receiver, element, ctx);
                return true;
            }
            if (receiver is ScriptCursor cursor)
            {
                if (call.memberId == NeoSequenceContract.MoveNext)
                {
                    value = cursor.MoveNext(ctx);
                    return true;
                }
                if (call.memberId == NeoSequenceContract.Dispose)
                {
                    cursor.Dispose(ctx);
                    return true;
                }
            }
            return false;
        }

        private static object? RetainSequenceCollectionType(object? value, TypeInfo type, Context ctx)
        {
            if (type is TupleTypeInfo tupleType && value is RuntimeTuple tuple)
            {
                object?[]? replacements = null;
                for (int index = 0; index < tuple.Count; index++)
                {
                    object? retained = RetainSequenceCollectionType(tuple[index], tupleType.elements[index].typeInfo, ctx);
                    if (!ReferenceEquals(retained, tuple[index]))
                    {
                        if (replacements is null)
                        {
                            replacements = new object?[tuple.Count];
                            for (int slot = 0; slot < tuple.Count; slot++)
                                replacements[slot] = tuple[slot];
                        }
                        replacements[index] = retained;
                    }
                }
                return replacements is null ? tuple : new RuntimeTuple(replacements, tuple.TypeInfo);
            }
            if (value is NeoObjectRecord or NeoScriptObject)
                return value;
            if (value is not (object?[] or LocalList or RuntimeSet or IDictionary<string, object?>))
                return value;
            return TagSequenceCollection(value, CloseContextType(type, ctx), ctx);
        }

        private sealed class EnumerableQuery
        {
            internal readonly object? Source;
            internal readonly TypeInfo ElementType;
            internal EnumerableQuery(object? source, TypeInfo elementType)
            {
                Source = source;
                ElementType = elementType;
            }
        }

        // Normalize built-in List query inputs to the common enumerable protocol.
        private static object? ListQueryReceiver(object? source, Context ctx) =>
            source is object?[]? QueryEnumerable(source, ctx) : source;

        private static EnumerableQuery QueryEnumerable(object? source, Context ctx) =>
            source as EnumerableQuery ?? new EnumerableQuery(source,
                source is ScriptSequence sequence ? sequence.ElementType :
                SequenceCollectionElement(source, ctx) ?? new UnknownTypeInfo { required = true });

        private static object? EvalSequenceConversion(SequenceConversionPointer conversion, NeoScriptScope scope, Context ctx)
        {
            object? source = EvalPointer(conversion.pointer, scope, ctx);
            if (conversion.optional && source is null)
                return null;
            if (conversion.operation == "retainCollectionType")
                return RetainSequenceCollectionType(source, conversion.collectionType!, ctx);
            if (conversion.operation == "queryEnumerable")
                return new EnumerableQuery(source, CloseContextType(conversion.elementType, ctx));
            object? cursor = conversion.operation == "collectEnumerator" ? source : AcquireSequenceCursor(source, conversion.elementType, ctx);
            if (conversion.operation == "getEnumerator")
                return cursor;
            var values = new List<object?>();
            bool cleanup = true;
            try
            {
                while (AdvanceSequenceCursor(cursor, ctx))
                    values.Add(CoerceNumericBinding(ReadSequenceCurrent(cursor, ctx), conversion.elementType, "sequence entry", ctx));
            }
            catch (Exception error)
            {
                cleanup = NeoScriptErrorClassification.IsAuthoredCatchable(error);
                throw;
            }
            finally
            {
                if (cleanup)
                    DisposeSequenceCursor(cursor, ctx);
                else if (cursor is ScriptCursor runtime)
                    runtime.Abandon();
            }
            return KeepDerived(values.Count == 0 ? new object?[0] : values.ToArray(), null, true);
        }

        private static string SequenceTypeLabel(TypeInfo type, Context ctx)
        {
            string name = type.type.ToString();
            if (type is InterfaceTypeInfo protocol)
            {
                if (ctx.client.TryGetInterface(protocol.interfaceId, out Interface? declaration))
                    name = declaration.name;
                if (protocol.typeArguments is { Count: > 0 } arguments)
                {
                    var labels = new List<string>(arguments.Count);
                    foreach (TypeInfo argument in arguments.Values)
                        labels.Add(SequenceTypeLabel(argument, ctx));
                    name += "<" + string.Join(", ", labels) + ">";
                }
            }
            else if (type is ClassTypeInfo schema && ctx.client.TryGetClass(schema.classId, out NeoSchemaClass? declaration))
                name = declaration.name;
            else if (type is CollectionTypeInfo collection)
                name += "<" + SequenceTypeLabel(collection.entryTypeInfo, ctx) + ">";
            else
                name = name.ToLowerInvariant();
            return type.required ? name : name + "?";
        }

        private static object EvalRange(RangePointer range, NeoScriptScope scope, Context ctx)
        {
            object? start = EvalPointer(range.start, scope, ctx);
            object? end = EvalPointer(range.end, scope, ctx);
            return new RangeSequence(ctx.client, RangeBound(start, "start"), RangeBound(end, "end"));
        }

        internal static long RangeBound(object? value, string name)
        {
            if (value is not double number || double.IsNaN(number) || Math.Truncate(number) != number || Math.Abs(number) > 9007199254740991d)
                throw new NSGetterRuntimeError($"Range {name} must be a safe int.");
            return (long)number;
        }
    }
}
