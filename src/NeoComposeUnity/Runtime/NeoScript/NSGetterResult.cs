// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

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
        public bool ok { get; }
        public object? value { get; }
        public string? error { get; }

        private NSGetterResult(bool ok, object? value, string? error)
        {
            this.ok = ok;
            this.value = value;
            this.error = error;
        }

        public static NSGetterResult Ok(object? value) => new(true, value, null);
        public static NSGetterResult Error(string message) => new(false, null, message);
    }

    /// <summary>
    /// Result of invoking a NeoScript property setter. A successful result
    /// may be <see cref="pending"/> when the setter suspended at a deferred
    /// native Function and will resume from its callback.
    /// </summary>
    public readonly struct NSSetterResult
    {
        public bool ok { get; }
        public bool pending { get; }
        public string? error { get; }

        private NSSetterResult(bool ok, bool pending, string? error)
        {
            this.ok = ok;
            this.pending = pending;
            this.error = error;
        }

        public static NSSetterResult Ok() => new(true, false, null);
        public static NSSetterResult Pending() => new(true, true, null);
        public static NSSetterResult Error(string message) => new(false, false, message);
    }
}
