// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>Typed, lazy bridges used by generated sequence signatures.</summary>
    public static class NeoSequences
    {
        internal interface IScriptSequenceView
        {
            NeoClient Owner
            {
                get;
            }
            object Source
            {
                get;
            }
        }

        public static TypeInfo ElementType<T>(NeoClient client, bool? required, params TypeInfo[] arguments)
        {
            TypeInfo type = client.TypeArguments.Of<T>();
            if (arguments.Length == 0 && (required is null || type.required == required))
                return type;
            type = type.ShallowClone();
            if (required is bool present)
                type.required = present;
            if (arguments.Length != 0)
            {
                if (type is InterfaceTypeInfo protocol && protocol.typeArguments is { } bindings)
                {
                    var closed = new Dictionary<string, TypeInfo>(bindings.Count);
                    int index = 0;
                    foreach (string key in bindings.Keys)
                        closed.Add(key, arguments[index++]);
                    protocol.typeArguments = closed;
                }
                else if (type is CollectionTypeInfo collection)
                    collection.entryTypeInfo = arguments[0];
                else if (type is TupleTypeInfo tuple)
                {
                    var elements = new TupleElementTypeInfo[arguments.Length];
                    for (int index = 0; index < elements.Length; index++)
                        elements[index] = new TupleElementTypeInfo { typeInfo = arguments[index] };
                    tuple.elements = elements;
                }
            }
            return type;
        }

        private static void RequireOwner(NeoClient expected, NeoClient actual)
        {
            if (actual.IsDisposed)
                throw new ObjectDisposedException(nameof(NeoClient));
            if (!ReferenceEquals(expected, actual))
                throw new InvalidOperationException("A sequence belongs to a different project client.");
        }

        private static object? KnownSource(NeoClient client, object source, TypeInfo element)
        {
            if (source is IScriptSequenceView view)
            {
                RequireOwner(client, view.Owner);
                return view.Source;
            }
            if (source is INeoCollectionView collection)
            {
                if (collection.CollectionClient is { } collectionClient)
                    RequireOwner(client, collectionClient);
                var node = collection.CollectionNode;
                if (node is null)
                    return null;
                using var operation = new Operation(client, node.ownership);
                if (node.value is null)
                    throw new InvalidOperationException("The collection has no value.");
                object value = NSGetterEvaluator.UnwrapRow(node.value, operation.Context, node.ownership)!;
                return NSGetterEvaluator.TagSequenceCollection(value,
                    new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = element }, operation.Context);
            }
            if (source is NeoGeneratedClassValue generated)
            {
                RequireOwner(client, generated.Client);
                using var operation = new Operation(client, generated.ValueOwnership);
                if (generated.valueId is not { } id || !client.TryGetValue(generated.ValueOwnership, id, out MemberValue? row))
                    throw new InvalidOperationException("The authored sequence has no value.");
                return NSGetterEvaluator.UnwrapRow(row, operation.Context, generated.ValueOwnership);
            }
            return null;
        }

        public static object? WriteEnumerable<T>(NeoClient client, IEnumerable<T>? source, TypeInfo element, Func<T, object?> write)
        {
            if (source is null)
                return null;
            return KnownSource(client, source, element) ?? new ForeignSequence<T>(client, source, element, write);
        }

        public static object? WriteEnumerator<T>(NeoClient client, IEnumerator<T>? source, TypeInfo element, Func<T, object?> write)
        {
            if (source is null)
                return null;
            return KnownSource(client, source, element) ?? new ForeignCursor<T>(client, source, element, write);
        }

        public static IEnumerable<T>? ReadEnumerable<T>(NeoClient client, object? source, TypeInfo element, Func<object?, T> read)
        {
            if (source is null)
                return null;
            return new EnumerableView<T>(client, source, element, read);
        }

        public static IEnumerator<T>? ReadEnumerator<T>(NeoClient client, object? source, TypeInfo element, Func<object?, T> read)
        {
            if (source is null)
                return null;
            return new EnumeratorView<T>(client, source, read);
        }

        // An unconstrained T or tuple entry is already boxed at this boundary.
        // Keep its CLR value for generic return projection; normalize elements
        // lazily against the closed script type without constructing CLR types.
        private interface IBoxedSequence
        {
            NeoClient Owner
            {
                get;
            }
            object Original
            {
                get;
            }
        }

        internal static bool TryReadBoxed<T>(NeoClient client, object? value, out T result)
        {
            if (value is IBoxedSequence boxed)
            {
                RequireOwner(client, boxed.Owner);
                if (boxed.Original is T original)
                {
                    result = original;
                    return true;
                }
            }
            result = default!;
            return false;
        }

        internal static object? WriteBoxed(NeoClient client, object? value, TypeInfo type)
        {
            if (value is null || value is NSGetterEvaluator.ScriptSequence or NSGetterEvaluator.ScriptCursor or NeoGeneratedClassValue)
                return value;
            string? id = type switch
            {
                InterfaceTypeInfo protocol => protocol.interfaceId,
                FunctionArgumentTypeInfo argument => argument.interfaceId,
                _ => null,
            };
            var arguments = type switch
            {
                InterfaceTypeInfo protocol => protocol.typeArguments,
                FunctionArgumentTypeInfo argument => argument.typeArguments,
                _ => null,
            };
            string key = id == NeoSequenceContract.Enumerable ? NeoSequenceContract.EnumerableElement : NeoSequenceContract.EnumeratorElement;
            if (arguments is null || !arguments.TryGetValue(key, out TypeInfo element))
                return value;
            if (id == NeoSequenceContract.Enumerable && value is IEnumerable enumerable)
                return new BoxedEnumerable(client, enumerable, element, KnownSource(client, value, element));
            if (id == NeoSequenceContract.Enumerator && value is IEnumerator enumerator)
                return new BoxedCursor(client, enumerator, element, KnownSource(client, value, element));
            return value;
        }

        private sealed class BoxedEnumerable : NSGetterEvaluator.ScriptSequence, IBoxedSequence
        {
            private readonly IEnumerable source;
            private readonly object? known;
            NeoClient IBoxedSequence.Owner => Owner;
            public object Original => source;
            internal BoxedEnumerable(NeoClient owner, IEnumerable source, TypeInfo element, object? known) : base(owner, element)
            {
                this.source = source;
                this.known = known;
            }
            protected override NSGetterEvaluator.ScriptCursor CreateCursor(NSGetterEvaluator.Context ctx)
            {
                if (known is not null)
                {
                    object cursor = NSGetterEvaluator.AcquireSequenceCursor(known, ElementType, ctx)!;
                    return cursor as NSGetterEvaluator.ScriptCursor ?? new BoxedCursor(Owner, null, ElementType, cursor);
                }
                try
                {
                    return new BoxedCursor(Owner, source.GetEnumerator(), ElementType, null);
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
        }

        private sealed class BoxedCursor : NSGetterEvaluator.ScriptCursor, IBoxedSequence
        {
            private readonly IEnumerator? source;
            private readonly object? known;
            private bool closed;
            NeoClient IBoxedSequence.Owner => Owner;
            public object Original => source ?? known!;
            internal BoxedCursor(NeoClient owner, IEnumerator? source, TypeInfo element, object? known) : base(owner, element)
            {
                this.source = source;
                this.known = known;
            }
            protected override bool ExternalPosition => true;
            protected override bool Advance(NSGetterEvaluator.Context ctx, out object? value)
            {
                value = null;
                try
                {
                    bool moved = known is null ? source!.MoveNext() : NSGetterEvaluator.AdvanceSequenceCursor(known, ctx);
                    if (!moved)
                        Close(ctx);
                    return moved;
                }
                catch (Exception error)
                {
                    if (NeoScriptErrorClassification.IsAuthoredCatchable(error))
                        Close(ctx);
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
            protected override object? ReadCurrent(NSGetterEvaluator.Context ctx)
            {
                try
                {
                    object? value = known is null ? source!.Current : NSGetterEvaluator.ReadSequenceCurrent(known, ctx);
                    return NeoScriptValueMarshaller.NormalizeResolved(Owner, ctx.valueOwnership, value, ElementType, ctx, "Sequence element");
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
            protected override void Close(NSGetterEvaluator.Context ctx)
            {
                if (closed)
                    return;
                closed = true;
                try
                {
                    if (known is not null)
                        NSGetterEvaluator.DisposeSequenceCursor(known, ctx);
                    else
                        (source as IDisposable)?.Dispose();
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
        }

        private sealed class EnumerableView<T> : IEnumerable<T>, IScriptSequenceView
        {
            public NeoClient Owner
            {
                get;
            }
            public object Source
            {
                get;
            }
            private readonly TypeInfo element;
            private readonly Func<object?, T> read;
            internal EnumerableView(NeoClient client, object source, TypeInfo element, Func<object?, T> read)
            {
                Owner = client;
                Source = source;
                this.element = element;
                this.read = read;
            }
            public IEnumerator<T> GetEnumerator()
            {
                using var operation = new Operation(Owner);
                object cursor = NSGetterEvaluator.AcquireSequenceCursor(Source, element, operation.Context)
                    ?? throw new InvalidOperationException("GetEnumerator returned null.");
                return new EnumeratorView<T>(Owner, cursor, read);
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class EnumeratorView<T> : IEnumerator<T>, IScriptSequenceView
        {
            public NeoClient Owner
            {
                get;
            }
            public object Source
            {
                get;
            }
            private readonly Func<object?, T> read;
            internal EnumeratorView(NeoClient client, object source, Func<object?, T> read)
            {
                Owner = client;
                Source = source;
                this.read = read;
            }
            public T Current
            {
                get
                {
                    using var operation = new Operation(Owner);
                    return read(NSGetterEvaluator.ReadSequenceCurrent(Source, operation.Context));
                }
            }
            object? IEnumerator.Current => Current;
            public bool MoveNext()
            {
                using var operation = new Operation(Owner);
                return NSGetterEvaluator.AdvanceSequenceCursor(Source, operation.Context);
            }
            public void Dispose()
            {
                using var operation = new Operation(Owner);
                NSGetterEvaluator.DisposeSequenceCursor(Source, operation.Context);
            }
            public void Reset() => throw new NotSupportedException("NeoScript cursors cannot be reset.");
        }

        private sealed class ForeignSequence<T> : NSGetterEvaluator.ScriptSequence
        {
            private readonly IEnumerable<T> source;
            private readonly Func<T, object?> write;
            internal ForeignSequence(NeoClient client, IEnumerable<T> source, TypeInfo element, Func<T, object?> write) : base(client, element)
            {
                this.source = source;
                this.write = write;
            }
            protected override NSGetterEvaluator.ScriptCursor CreateCursor(NSGetterEvaluator.Context ctx)
            {
                try
                {
                    return new ForeignCursor<T>(Owner, source.GetEnumerator(), ElementType, write);
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
        }

        private sealed class ForeignCursor<T> : NSGetterEvaluator.ScriptCursor
        {
            private IEnumerator<T>? source;
            private readonly Func<T, object?> write;
            internal ForeignCursor(NeoClient client, IEnumerator<T> source, TypeInfo element, Func<T, object?> write) : base(client, element)
            {
                this.source = source;
                this.write = write;
            }
            protected override bool Advance(NSGetterEvaluator.Context ctx, out object? value)
            {
                value = null;
                try
                {
                    bool moved = source!.MoveNext();
                    if (!moved)
                        Close(ctx);
                    return moved;
                }
                catch (Exception error)
                {
                    if (NeoScriptErrorClassification.IsAuthoredCatchable(error))
                        Close(ctx);
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
            protected override bool ExternalPosition => true;
            protected override object? ReadCurrent(NSGetterEvaluator.Context ctx)
            {
                try
                {
                    return NeoScriptValueMarshaller.NormalizeResolved(ctx.client, ctx.valueOwnership,
                        write(source!.Current), ElementType, ctx, "Sequence element");
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
            protected override void Close(NSGetterEvaluator.Context ctx)
            {
                IEnumerator<T>? disposing = source;
                source = null;
                try
                {
                    disposing?.Dispose();
                }
                catch (Exception error)
                {
                    throw NSGetterEvaluator.NormalizeError(error, ctx);
                }
            }
            protected override void Release() => source = null;
        }

        private readonly struct Operation : IDisposable
        {
            internal readonly NSGetterEvaluator.Context Context;
            private readonly NSGetterEvaluator.Context? previous;
            private readonly NeoClient client;
            internal Operation(NeoClient client, NeoValueOwnership ownership = NeoValueOwnership.Session)
            {
                if (client.IsDisposed)
                    throw new ObjectDisposedException(nameof(NeoClient));
                this.client = client;
                previous = client.ActiveScriptContext;
                Context = previous ?? client.RentDirectFunctionContext(ownership, function: true);
                client.ActiveScriptContext = Context;
                client.EnterScriptWrites();
            }
            public void Dispose()
            {
                try
                {
                    client.ExitScriptWrites();
                }
                finally
                {
                    client.ActiveScriptContext = previous;
                    if (previous is null)
                        client.ReturnDirectFunctionContext(Context, null);
                }
            }
        }
    }
}
