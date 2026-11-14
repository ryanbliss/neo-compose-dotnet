// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// Result of evaluating an NSProperty — either a successful computed
    /// value or an error string suitable for inline display. Mirrors
    /// the TS-side <c>INSGetterResult</c>.
    ///
    /// <para>Pattern-match via <see cref="ok"/>:
    /// <code>
    /// var r = nsGetter.Compute();
    /// if (r.ok) UseValue(r.value);
    /// else      LogError(r.error);
    /// </code></para>
    /// </summary>
    public readonly struct NSGetterResult
    {
        public bool ok
        {
            get;
        }
        public object? value
        {
            get;
        }
        // A failure formats its message only when read: a caller that treats
        // the error as absence never pays for the trace.
        public string? error => message ?? (failure is null ? null : NeoScriptDebug.FormatError(failure));

        private readonly string? message;

        internal Exception? failure
        {
            get;
        }

        public void ThrowIfFailed()
        {
            if (!ok)
                throw failure ?? new InvalidOperationException(error ?? "NSProperty getter evaluation failed.");
        }

        private NSGetterResult(bool ok, object? value, string? error, Exception? failure = null)
        {
            this.ok = ok;
            this.value = value;
            message = error;
            this.failure = failure;
        }

        // Storing a constant null error skips that store's write barrier.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NSGetterResult(object? value)
        {
            ok = true;
            this.value = value;
            message = null;
            failure = null;
        }

        // Inlined, so a result returned through the getter's frames is built
        // once in place rather than copied out of each call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NSGetterResult Ok(object? value) => new(value);
        public static NSGetterResult Error(string message) => new(false, null, message);
        internal static NSGetterResult Error(string message, Exception failure) => new(false, null, message, failure);
        internal static NSGetterResult Error(Exception failure) => new(false, null, null, failure);
    }

    /// <summary>
    /// Result of invoking a NeoScript property setter. A successful result
    /// may be <see cref="pending"/> when the setter suspended at a deferred
    /// native Function and will resume from its callback.
    /// </summary>
    public readonly struct NSSetterResult
    {
        public bool ok
        {
            get;
        }
        public bool pending
        {
            get;
        }
        public string? error
        {
            get;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NSSetterResult(bool ok, bool pending, string? error)
        {
            this.ok = ok;
            this.pending = pending;
            this.error = error;
        }

        // Inlined, so a result returned through the setter's frames is built
        // once in place rather than copied out of each call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NSSetterResult Ok() => new(true, false, null);
        public static NSSetterResult Pending() => new(true, true, null);
        public static NSSetterResult Error(string message) => new(false, false, message);
    }
}
