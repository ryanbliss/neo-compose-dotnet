// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Transport for the Better Auth device-authorization endpoints. Abstracted
    /// so the flow can be unit-tested with a fake transport.
    /// </summary>
    public interface INeoComposeDeviceAuthTransport
    {
        Task<NeoComposeDeviceCodeResponse> RequestDeviceCodeAsync(
            string apiBaseUrl,
            string clientId,
            string scope,
            CancellationToken cancellationToken);

        Task<NeoComposeDevicePollResult> PollDeviceTokenAsync(
            string apiBaseUrl,
            string clientId,
            string deviceCode,
            CancellationToken cancellationToken);

        Task<NeoComposeUserProfile> GetProfileAsync(
            string apiBaseUrl,
            string accessToken,
            CancellationToken cancellationToken);
    }

    public sealed class NeoComposeDeviceAuthTransport : INeoComposeDeviceAuthTransport
    {
        private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";

        public async Task<NeoComposeDeviceCodeResponse> RequestDeviceCodeAsync(
            string apiBaseUrl,
            string clientId,
            string scope,
            CancellationToken cancellationToken)
        {
            // The device flow is a fresh, cookie-less OAuth exchange. UnityWebRequest
            // keeps a shared editor cookie jar, so a stale Better Auth session cookie
            // left over from a previous sign-in/sign-out would be replayed here and
            // rejected with a 403 by the device-code endpoint. Clear the origin's
            // cookies first so each device-code request starts clean.
            ClearOriginCookies(apiBaseUrl);

            var body = JsonConvert.SerializeObject(new { client_id = clientId, scope });
            var response = await NeoComposeWebRequests.SendAsync(
                NeoComposeAuthEndpoints.DeviceCodeUrl(apiBaseUrl),
                "POST",
                body,
                cancellationToken: cancellationToken);

            if (response.IsConnectionError)
            {
                throw new NeoComposeDeviceAuthException(
                    $"Could not reach Neo Compose to start sign-in: {response.Error}");
            }

            if (!response.IsSuccessStatus)
            {
                var error = TryDeserialize<NeoComposeDeviceErrorResponse>(response.Text);
                throw new NeoComposeDeviceAuthException(
                    error != null && error.errorDescription.Length > 0
                        ? error.errorDescription
                        : $"Neo Compose rejected the sign-in request ({response.StatusCode}).");
            }

            var code = TryDeserialize<NeoComposeDeviceCodeResponse>(response.Text);
            if (code == null || code.deviceCode.Length == 0)
            {
                throw new NeoComposeDeviceAuthException("Neo Compose returned an empty device code response.");
            }

            return code;
        }

        public async Task<NeoComposeDevicePollResult> PollDeviceTokenAsync(
            string apiBaseUrl,
            string clientId,
            string deviceCode,
            CancellationToken cancellationToken)
        {
            var body = JsonConvert.SerializeObject(new
            {
                grant_type = DeviceGrantType,
                device_code = deviceCode,
                client_id = clientId,
            });
            NeoComposeWebResponse response;
            try
            {
                response = await NeoComposeWebRequests.SendAsync(
                    NeoComposeAuthEndpoints.DeviceTokenUrl(apiBaseUrl),
                    "POST",
                    body,
                    cancellationToken: cancellationToken);
            }
            catch (TimeoutException)
            {
                return NeoComposeDevicePollResult.Retry(
                    "The device authorization poll timed out; Neo Compose will retry.");
            }

            if (response.IsConnectionError)
            {
                return NeoComposeDevicePollResult.Retry(
                    $"Could not reach Neo Compose while waiting for approval: {response.Error}");
            }

            if (response.IsSuccessStatus)
            {
                var token = TryDeserialize<NeoComposeDeviceTokenSuccess>(response.Text);
                if (token == null || token.accessToken.Length == 0)
                {
                    return NeoComposeDevicePollResult.Error("Neo Compose returned an empty token response.");
                }

                return NeoComposeDevicePollResult.Success(token);
            }

            var error = TryDeserialize<NeoComposeDeviceErrorResponse>(response.Text);
            return MapPollError(error, response, DateTimeOffset.UtcNow);
        }

        public async Task<NeoComposeUserProfile> GetProfileAsync(
            string apiBaseUrl,
            string accessToken,
            CancellationToken cancellationToken)
        {
            // Identity is UI-only; a failure here must not fail authorization.
            var response = await NeoComposeWebRequests.SendAsync(
                NeoComposeAuthEndpoints.GetSessionUrl(apiBaseUrl),
                "GET",
                bearerToken: accessToken,
                cancellationToken: cancellationToken);

            if (response.IsConnectionError || !response.IsSuccessStatus) return NeoComposeUserProfile.Empty;

            var session = TryDeserialize<NeoComposeSessionResponse>(response.Text);
            if (session?.user == null) return NeoComposeUserProfile.Empty;

            return new NeoComposeUserProfile(session.user.name, session.user.email);
        }

        private static void ClearOriginCookies(string apiBaseUrl)
        {
            if (Uri.TryCreate(NeoComposeAuthEndpoints.Origin(apiBaseUrl), UriKind.Absolute, out var origin))
            {
                UnityWebRequest.ClearCookieCache(origin);
            }
        }

        internal static NeoComposeDevicePollResult MapPollError(
            NeoComposeDeviceErrorResponse? error,
            NeoComposeWebResponse response,
            DateTimeOffset now)
        {
            if (response.StatusCode == 429 ||
                (response.StatusCode >= 500 && response.StatusCode <= 599))
            {
                var transientDescription = error != null && error.errorDescription.Length > 0
                    ? error.errorDescription
                    : $"Neo Compose returned a transient error ({response.StatusCode}).";
                return NeoComposeDevicePollResult.Retry(
                    transientDescription,
                    ParseRetryAfterSeconds(response, now));
            }

            switch (error?.error)
            {
                case "authorization_pending":
                    return NeoComposeDevicePollResult.Pending();
                case "slow_down":
                    return NeoComposeDevicePollResult.SlowDown();
                case "access_denied":
                    return NeoComposeDevicePollResult.Denied();
                case "expired_token":
                    return NeoComposeDevicePollResult.Expired();
                default:
                    var description = error != null && error.errorDescription.Length > 0
                        ? error.errorDescription
                        : $"Neo Compose returned an unexpected error ({response.StatusCode}).";

                    return NeoComposeDevicePollResult.Error(description);
            }
        }

        private static int ParseRetryAfterSeconds(
            NeoComposeWebResponse response,
            DateTimeOffset now)
        {
            var raw = response.GetHeader("Retry-After");
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return Math.Max(0, seconds);
            }

            if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var retryAt))
            {
                return Math.Max(0, (int)Math.Ceiling((retryAt - now).TotalSeconds));
            }

            return 0;
        }

        private static T? TryDeserialize<T>(string text)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonConvert.DeserializeObject<T>(text);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
