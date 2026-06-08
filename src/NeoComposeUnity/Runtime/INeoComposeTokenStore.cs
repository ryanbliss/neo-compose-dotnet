// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Persists the signed-in Neo Compose user token. The access token is held
    /// only by a platform secret backend, while non-secret hints are held
    /// separately so auth UI can render without unlocking the secret store.
    /// </summary>
    /// <remarks>
    /// Implemented by the editor (OS-native secret store) and by the runtime
    /// (<see cref="NeoMultiPlatformTokenStore"/>). Lives in the runtime asmdef so
    /// the shared device-flow / refresh core can depend on the abstraction
    /// without referencing an editor-only concrete.
    /// </remarks>
    public interface INeoComposeTokenStore
    {
        NeoComposeStoredToken? Load();
        void Save(NeoComposeStoredToken token);
        void Clear();
        NeoComposeTokenHint? PeekHint();
    }

    /// <summary>
    /// Stores small, non-secret hint strings keyed by a stable key. Must never
    /// hold the access token.
    /// </summary>
    public interface INeoComposeTokenHintStore
    {
        string? Read(string key);
        void Write(string key, string value);
        void Delete(string key);
    }
}
