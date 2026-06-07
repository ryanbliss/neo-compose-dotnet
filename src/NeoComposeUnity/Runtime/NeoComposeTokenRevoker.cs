// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;


namespace NeoCompose.Runtime
{
    /// <summary>
    /// Best-effort server-side revocation of a Neo Compose Unity token.
    /// </summary>
    public interface INeoComposeTokenRevoker
    {
        Task RevokeAsync(string apiBaseUrl, string accessToken);
    }

    /// <summary>
    /// Revokes the device-flow token by signing the session out. The Unity
    /// device token is a Better Auth session-backed bearer token, so
    /// <c>POST /api/auth/sign-out</c> invalidates it immediately. There is no
    /// RFC 7009 token-revocation endpoint to use instead.
    /// </summary>
    public sealed class NeoComposeTokenRevoker : INeoComposeTokenRevoker
    {
        public async Task RevokeAsync(string apiBaseUrl, string accessToken)
        {
            await NeoComposeWebRequests.SendAsync(
                NeoComposeAuthEndpoints.SignOutUrl(apiBaseUrl),
                "POST",
                "{}",
                accessToken);
        }
    }
}
