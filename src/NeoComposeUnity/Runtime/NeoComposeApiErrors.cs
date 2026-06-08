// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Describes the API operation behind a request so authorization failures
    /// can produce a specific, actionable message. Shared by the editor and
    /// runtime API clients.
    /// </summary>
    public readonly struct NeoComposeApiOperation
    {
        public NeoComposeApiOperation(string description, string? projectId = null, string? requiredScope = null)
        {
            Description = description;
            ProjectId = projectId;
            RequiredScope = requiredScope;
        }

        /// <summary>Imperative description, e.g. "edit this project's Unity settings".</summary>
        public string Description { get; }

        public string? ProjectId { get; }

        /// <summary>The OAuth/project scope the operation needs, when known.</summary>
        public string? RequiredScope { get; }
    }

    /// <summary>
    /// Thrown when the user is authenticated but lacks permission for the
    /// requested operation (HTTP 403). The user remains signed in; this is not an
    /// authentication failure and must not trigger re-authentication or a retry.
    /// </summary>
    public sealed class NeoComposeApiAuthorizationException : Exception
    {
        public NeoComposeApiAuthorizationException(
            string message,
            string? projectId,
            string? requiredScope)
            : base(message)
        {
            ProjectId = projectId;
            RequiredScope = requiredScope;
        }

        public string? ProjectId { get; }
        public string? RequiredScope { get; }
    }

    /// <summary>
    /// Thrown when the server could not resolve the requested resource (HTTP
    /// 404) — e.g. archiving or reading a save that no longer exists in the
    /// cloud (it was deleted server-side while a stale copy lingers locally).
    /// Callers that hold a local fallback (the project store's delete path) can
    /// catch this to proceed with local cleanup rather than surfacing a hard
    /// failure.
    /// </summary>
    public sealed class NeoComposeNotFoundException : Exception
    {
        public NeoComposeNotFoundException(string message)
            : base(message)
        {
        }
    }
}
