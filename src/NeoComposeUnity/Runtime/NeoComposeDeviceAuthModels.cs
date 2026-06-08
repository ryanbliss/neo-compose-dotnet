// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Response from <c>POST /api/auth/device/code</c>.
    /// </summary>
    public sealed class NeoComposeDeviceCodeResponse
    {
        [JsonProperty("device_code")] public string deviceCode = "";
        [JsonProperty("user_code")] public string userCode = "";
        [JsonProperty("verification_uri")] public string verificationUri = "";
        [JsonProperty("verification_uri_complete")] public string verificationUriComplete = "";
        [JsonProperty("expires_in")] public int expiresInSeconds;
        [JsonProperty("interval")] public int intervalSeconds;
    }

    /// <summary>
    /// Success response from <c>POST /api/auth/device/token</c>.
    /// </summary>
    public sealed class NeoComposeDeviceTokenSuccess
    {
        [JsonProperty("access_token")] public string accessToken = "";
        [JsonProperty("token_type")] public string tokenType = "";
        [JsonProperty("expires_in")] public int expiresInSeconds;
        [JsonProperty("scope")] public string scope = "";
    }

    /// <summary>
    /// Error payload shared by device-flow endpoints (RFC 8628).
    /// </summary>
    public sealed class NeoComposeDeviceErrorResponse
    {
        [JsonProperty("error")] public string error = "";
        [JsonProperty("error_description")] public string errorDescription = "";
    }

    /// <summary>
    /// Subset of <c>GET /api/auth/get-session</c> used for the signed-in
    /// identity hint.
    /// </summary>
    public sealed class NeoComposeSessionResponse
    {
        [JsonProperty("session")] public NeoComposeSessionMetadata? session;
        [JsonProperty("user")] public NeoComposeProfileUser? user;
    }

    public sealed class NeoComposeSessionMetadata
    {
        [JsonProperty("expiresAt")] public string expiresAt = "";
        [JsonProperty("updatedAt")] public string updatedAt = "";
    }

    public sealed class NeoComposeProfileUser
    {
        [JsonProperty("name")] public string name = "";
        [JsonProperty("email")] public string email = "";
    }

    public sealed class NeoComposeUserProfile
    {
        public NeoComposeUserProfile(string name, string email)
        {
            this.name = name;
            this.email = email;
        }

        public string name { get; }
        public string email { get; }

        public static NeoComposeUserProfile Empty => new NeoComposeUserProfile("", "");
    }

    public enum NeoComposeDevicePollStatus
    {
        Pending,
        SlowDown,
        Success,
        Denied,
        Expired,
        Error,
    }

    /// <summary>
    /// Result of a single device-token poll.
    /// </summary>
    public sealed class NeoComposeDevicePollResult
    {
        private NeoComposeDevicePollResult(
            NeoComposeDevicePollStatus status,
            NeoComposeDeviceTokenSuccess? token,
            string message)
        {
            this.status = status;
            this.token = token;
            this.message = message;
        }

        public NeoComposeDevicePollStatus status { get; }
        public NeoComposeDeviceTokenSuccess? token { get; }
        public string message { get; }

        public static NeoComposeDevicePollResult Pending() =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.Pending, null, "");

        public static NeoComposeDevicePollResult SlowDown() =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.SlowDown, null, "");

        public static NeoComposeDevicePollResult Success(NeoComposeDeviceTokenSuccess token) =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.Success, token, "");

        public static NeoComposeDevicePollResult Denied() =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.Denied, null, "");

        public static NeoComposeDevicePollResult Expired() =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.Expired, null, "");

        public static NeoComposeDevicePollResult Error(string message) =>
            new NeoComposeDevicePollResult(NeoComposeDevicePollStatus.Error, null, message);
    }

    public enum NeoComposeDeviceAuthOutcome
    {
        Success,
        Denied,
        Expired,
        TimedOut,
        Canceled,
        Failed,
    }

    /// <summary>
    /// Result of an end-to-end device authorization attempt.
    /// </summary>
    public sealed class NeoComposeDeviceAuthResult
    {
        private NeoComposeDeviceAuthResult(
            NeoComposeDeviceAuthOutcome outcome,
            NeoComposeStoredToken? token,
            string message)
        {
            this.outcome = outcome;
            this.token = token;
            this.message = message;
        }

        public NeoComposeDeviceAuthOutcome outcome { get; }
        public NeoComposeStoredToken? token { get; }
        public string message { get; }

        public bool IsSuccess => outcome == NeoComposeDeviceAuthOutcome.Success;

        public static NeoComposeDeviceAuthResult Success(NeoComposeStoredToken token) =>
            new NeoComposeDeviceAuthResult(NeoComposeDeviceAuthOutcome.Success, token, "");

        public static NeoComposeDeviceAuthResult Failed(
            NeoComposeDeviceAuthOutcome outcome,
            string message) =>
            new NeoComposeDeviceAuthResult(outcome, null, message);
    }

    /// <summary>
    /// Thrown for unexpected device-flow transport failures (connection errors,
    /// malformed responses, or unrecognized error payloads).
    /// </summary>
    public sealed class NeoComposeDeviceAuthException : Exception
    {
        public NeoComposeDeviceAuthException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Resolves the single-origin Better Auth endpoints and the web verification
    /// page from the configured API base URL.
    /// </summary>
    public static class NeoComposeAuthEndpoints
    {
        public static string Origin(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }

            return apiBaseUrl.Trim().TrimEnd('/');
        }

        public static string AuthApiBase(string apiBaseUrl) =>
            Origin(apiBaseUrl) + NeoComposeDefaults.AuthBasePath;

        public static string DeviceCodeUrl(string apiBaseUrl) => AuthApiBase(apiBaseUrl) + "/device/code";

        public static string DeviceTokenUrl(string apiBaseUrl) => AuthApiBase(apiBaseUrl) + "/device/token";

        public static string GetSessionUrl(string apiBaseUrl) => AuthApiBase(apiBaseUrl) + "/get-session";

        public static string SignOutUrl(string apiBaseUrl) => AuthApiBase(apiBaseUrl) + "/sign-out";

        /// <summary>
        /// Resolves the verification URI to open in the browser. Prefers the
        /// server-provided complete URI; otherwise resolves the (possibly
        /// relative) verification URI against the origin.
        /// </summary>
        public static string ResolveVerificationUri(
            string apiBaseUrl,
            NeoComposeDeviceCodeResponse response)
        {
            if (!string.IsNullOrWhiteSpace(response.verificationUriComplete))
            {
                return ResolveAgainstOrigin(apiBaseUrl, response.verificationUriComplete);
            }

            if (!string.IsNullOrWhiteSpace(response.verificationUri))
            {
                return ResolveAgainstOrigin(apiBaseUrl, response.verificationUri);
            }

            return Origin(apiBaseUrl) + NeoComposeDefaults.DeviceVerificationPath +
                "?user_code=" + UnityWebRequestEscape(response.userCode);
        }

        private static string ResolveAgainstOrigin(string apiBaseUrl, string uri)
        {
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return Origin(apiBaseUrl) + (uri.StartsWith("/") ? uri : "/" + uri);
        }

        private static string UnityWebRequestEscape(string value) =>
            UnityEngine.Networking.UnityWebRequest.EscapeURL(value);
    }
}
