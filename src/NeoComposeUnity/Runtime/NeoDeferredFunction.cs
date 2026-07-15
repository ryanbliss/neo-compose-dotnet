// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;

namespace NeoCompose.Runtime
{
    public abstract class NeoDeferredFunctionBase
    {
        private readonly NeoDeferredFunctionState state;

        public bool Pending => state.Pending;
        public CancellationToken CancellationToken => state.CancellationToken;

        internal NeoDeferredFunctionBase(
            string memberId,
            string functionName,
            Action<object?> complete,
            Action<Exception> fail,
            Action<string>? dispose = null)
            : this(new NeoDeferredFunctionState(
                memberId,
                functionName,
                complete,
                fail,
                dispose))
        {
        }

        internal NeoDeferredFunctionBase(NeoDeferredFunctionState state)
        {
            this.state = state;
        }

        public string MemberId => state.MemberId;
        public string FunctionName => state.FunctionName;

        public void Fail(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            state.Fail(exception);
        }

        protected void CompleteUntyped(object? value)
        {
            state.Complete(value);
        }

        internal void DisposeFromOwner(string reason)
        {
            state.DisposeFromOwner(reason);
        }

        internal NeoDeferredFunctionState StateCore => state;
    }

    internal sealed class NeoDeferredFunctionState
    {
        private readonly Action<object?> complete;
        private readonly Action<Exception> fail;
        private readonly Action<string>? dispose;
        private readonly CancellationTokenSource cancellation = new();
        private bool pending = true;
        private bool disposed;

        internal NeoDeferredFunctionState(
            string memberId,
            string functionName,
            Action<object?> complete,
            Action<Exception> fail,
            Action<string>? dispose = null)
        {
            MemberId = memberId;
            FunctionName = functionName;
            this.complete = complete;
            this.fail = fail;
            this.dispose = dispose;
        }

        public string MemberId { get; }
        public string FunctionName { get; }
        public bool Pending => pending && !disposed;
        public CancellationToken CancellationToken => cancellation.Token;

        internal void Complete(object? value)
        {
            EnsurePending("complete");
            pending = false;
            complete(value);
        }

        internal void Fail(Exception exception)
        {
            EnsurePending("fail");
            pending = false;
            fail(exception);
        }

        internal void DisposeFromOwner(string reason)
        {
            if (!pending || disposed) return;
            disposed = true;
            pending = false;
            cancellation.Cancel();
            dispose?.Invoke(reason);
        }

        private void EnsurePending(string operation)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(NeoDeferredFunctionBase),
                    $"Cannot {operation} deferred Function '{FunctionName}' because its owning dialogue was disposed.");
            }
            if (!pending)
            {
                throw new InvalidOperationException(
                    $"Cannot {operation} deferred Function '{FunctionName}' because it has already completed.");
            }
        }
    }

    public sealed class NeoDeferredFunction : NeoDeferredFunctionBase
    {
        internal NeoDeferredFunction(
            string memberId,
            string functionName,
            Action<object?> complete,
            Action<Exception> fail,
            Action<string>? dispose = null)
            : base(memberId, functionName, complete, fail, dispose)
        {
        }

        internal NeoDeferredFunction(NeoDeferredFunctionState state)
            : base(state)
        {
        }

        public void Complete()
        {
            CompleteUntyped(null);
        }
    }

    public sealed class NeoDeferredFunction<T> : NeoDeferredFunctionBase
    {
        internal NeoDeferredFunction(
            string memberId,
            string functionName,
            Action<object?> complete,
            Action<Exception> fail,
            Action<string>? dispose = null)
            : base(memberId, functionName, complete, fail, dispose)
        {
        }

        internal NeoDeferredFunction(NeoDeferredFunctionState state)
            : base(state)
        {
        }

        public void Complete(T value)
        {
            CompleteUntyped(value);
        }
    }

    public sealed class NeoDeferredFunctionRuntimeError : NeoScript.NSGetterRuntimeError
    {
        public NeoDeferredFunctionRuntimeError(string message)
            : base(message)
        {
        }
    }
}
