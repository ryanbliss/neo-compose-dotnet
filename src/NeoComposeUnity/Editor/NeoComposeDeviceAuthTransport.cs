// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NeoCompose.Unity.Editor
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
            var response = await NeoComposeWebRequests.SendAsync(
                NeoComposeAuthEndpoints.DeviceTokenUrl(apiBaseUrl),
                "POST",
                body,
                cancellationToken: cancellationToken);

            if (response.IsConnectionError)
            {
                return NeoComposeDevicePollResult.Error(
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
            return MapPollError(error, response.StatusCode);
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

        private static NeoComposeDevicePollResult MapPollError(
            NeoComposeDeviceErrorResponse? error,
            long statusCode)
        {
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
                        : $"Neo Compose returned an unexpected error ({statusCode}).";
                    return NeoComposeDevicePollResult.Error(description);
            }
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
