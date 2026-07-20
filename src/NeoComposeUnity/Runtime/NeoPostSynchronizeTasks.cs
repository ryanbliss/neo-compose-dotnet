// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

#if UNITY_EDITOR
using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Declares durable editor work from a generated value's
    /// <see cref="NeoGeneratedClassValue.OnDidSynchronize"/> callback. Requests
    /// contain stable identifiers only; Editor integrations register handlers
    /// for each kind separately.
    /// </summary>
    public static class NeoPostSynchronizeTasks
    {
        private static Action<NeoPostSynchronizeTaskRequest>? requestSink;

        /// <summary>
        /// Requests one generated-artifact task for the active synchronization.
        /// Kinds use lowercase letters and digits separated by dots or hyphens,
        /// for example <c>navigation.grid-artifact</c>.
        /// </summary>
        public static void Request(string kind, string ownerValueId, string name)
        {
            ValidateKind(kind, nameof(kind));
            if (string.IsNullOrWhiteSpace(ownerValueId))
            {
                throw new ArgumentException(
                    "Post-sync task owner value id cannot be empty.",
                    nameof(ownerValueId));
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Post-sync task name cannot be empty.",
                    nameof(name));
            }

            var sink = requestSink ?? throw new InvalidOperationException(
                "NeoPostSynchronizeTasks.Request may only be called while Neo Compose is " +
                "collecting post-sync lifecycle work.");
            sink(new NeoPostSynchronizeTaskRequest(
                kind,
                ownerValueId.Trim(),
                name.Trim()));
        }

        internal static IDisposable BeginCollection(
            Action<NeoPostSynchronizeTaskRequest> collect)
        {
            if (collect == null) throw new ArgumentNullException(nameof(collect));
            if (requestSink != null)
            {
                throw new InvalidOperationException(
                    "Neo Compose is already collecting post-sync task requests.");
            }

            requestSink = collect;
            return new CollectionLease(collect);
        }

        internal static void ValidateKind(string kind, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException(
                    "Post-sync task kind cannot be empty.",
                    parameterName);
            }

            string value = kind.Trim();
            bool previousWasSeparator = true;
            for (int index = 0; index < value.Length; index += 1)
            {
                char character = value[index];
                bool isLetter = character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                bool isSeparator = character == '.' || character == '-';
                if ((!isLetter && !isDigit && !isSeparator) ||
                    (isSeparator && previousWasSeparator))
                {
                    throw InvalidKind(parameterName);
                }
                previousWasSeparator = isSeparator;
            }
            if (previousWasSeparator) throw InvalidKind(parameterName);
        }

        private static ArgumentException InvalidKind(string parameterName) =>
            new(
                "Post-sync task kind must contain lowercase letters or digits, with " +
                "single dots or hyphens as separators.",
                parameterName);

        private sealed class CollectionLease : IDisposable
        {
            private Action<NeoPostSynchronizeTaskRequest>? sink;

            public CollectionLease(Action<NeoPostSynchronizeTaskRequest> sink)
            {
                this.sink = sink;
            }

            public void Dispose()
            {
                var owned = sink;
                if (owned == null) return;
                sink = null;
                if (ReferenceEquals(requestSink, owned)) requestSink = null;
            }
        }
    }

    internal sealed class NeoPostSynchronizeTaskRequest
    {
        internal NeoPostSynchronizeTaskRequest(
            string kind,
            string ownerValueId,
            string name)
        {
            Kind = kind.Trim();
            OwnerValueId = ownerValueId;
            Name = name;
        }

        internal string Kind { get; }
        internal string OwnerValueId { get; }
        internal string Name { get; }
    }
}
#endif
