// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Holds the project-scoped runtime API key, kept in a <b>separate, gitignored</b>
    /// Resources asset rather than the committed <see cref="NeoComposeConfig"/>.
    /// </summary>
    /// <remarks>
    /// The key is a low-trust, read-only, project-scoped secret. It is intentionally
    /// bundled into the built player (it must be available at runtime), but it should
    /// never be committed to source control — so a public repo doesn't leak the key
    /// before the game ships. The synchronize flow auto-creates this asset and a
    /// per-directory <c>.gitignore</c> that excludes it; on CI a build processor can
    /// populate it from the <c>NEO_COMPOSE_RUNTIME_API_KEY</c> environment variable.
    ///
    /// <para>Because it is gitignored, a fresh checkout has no asset until the
    /// developer pastes the key in the editor (or the env var is set at build time);
    /// the runtime treats a missing secret the same as an empty key.</para>
    /// </remarks>
    public sealed class NeoComposeRuntimeSecret : ScriptableObject
    {
        [SerializeField]
        private string runtimeApiKey = "";

        /// <summary>The project-scoped runtime API key, or empty when unset.</summary>
        public string RuntimeApiKey
        {
            get => runtimeApiKey;
            set => runtimeApiKey = value ?? "";
        }

        /// <summary>
        /// Loads the bundled runtime secret from Resources, or null when none has
        /// been created/populated (e.g. a fresh gitignored checkout). Callers treat
        /// null as "no key".
        /// </summary>
        public static NeoComposeRuntimeSecret? LoadDefault() =>
            Resources.Load<NeoComposeRuntimeSecret>(NeoComposeDefaults.RuntimeSecretResourcePath);

        /// <summary>The resolved runtime API key from the bundled secret, or empty.</summary>
        public static string LoadRuntimeApiKey() => LoadDefault()?.RuntimeApiKey ?? "";
    }
}
