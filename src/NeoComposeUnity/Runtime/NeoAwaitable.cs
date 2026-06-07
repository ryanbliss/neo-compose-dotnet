// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Helpers for producing already-completed <see cref="Awaitable"/> /
    /// <see cref="Awaitable{T}"/> values — the <c>Awaitable</c> equivalent of
    /// <c>Task.FromResult</c> / <c>Task.CompletedTask</c>. Used by the synchronous
    /// (in-memory / file) save-stack seams so their async signatures complete without
    /// suspending the player loop, and available to developers implementing the
    /// <see cref="INeoLocalSaveStore"/> / <see cref="IProjectDataSource"/> seams.
    /// </summary>
    public static class NeoAwaitable
    {
        public static Awaitable<T> FromResult<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        public static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
