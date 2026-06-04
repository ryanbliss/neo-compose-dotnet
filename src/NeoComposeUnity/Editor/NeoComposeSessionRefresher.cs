// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NeoCompose.Unity.Editor
{
    public interface INeoComposeSessionRefresher
    {
        Task<bool> RefreshIfDueAsync(string apiBaseUrl);
    }

    public sealed class NeoComposeSessionRefresher : INeoComposeSessionRefresher
    {
        private const string RefreshedTokenHeader = "set-auth-token";
        private const long DefaultBetterAuthUpdateAgeSeconds = 24 * 60 * 60;
        private const long MinimumRefreshCheckIntervalSeconds = 60 * 60;

        private readonly Func<string, INeoComposeTokenStore> storeFactory;
        private readonly INeoComposeHttpClient httpClient;
        private readonly Func<DateTimeOffset> now;

        public NeoComposeSessionRefresher(
            Func<string, INeoComposeTokenStore>? storeFactory = null,
            INeoComposeHttpClient? httpClient = null,
            Func<DateTimeOffset>? now = null)
        {
            this.storeFactory = storeFactory ?? (apiBaseUrl => NeoComposeTokenStore.Create(apiBaseUrl));
            this.httpClient = httpClient ?? new NeoComposeUnityHttpClient();
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task<bool> RefreshIfDueAsync(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) return false;

            var store = storeFactory(apiBaseUrl);
            var token = store.Load();
            if (token == null || !token.HasAccessToken) return false;

            var nowUnixSeconds = now().ToUnixTimeSeconds();
            if (nowUnixSeconds >= token.expiresAtUnixSeconds) return false;
            if (nowUnixSeconds < token.updatedAtUnixSeconds + DefaultBetterAuthUpdateAgeSeconds) return false;
            if (nowUnixSeconds < token.sessionCheckedAtUnixSeconds + MinimumRefreshCheckIntervalSeconds) return false;

            var response = await httpClient.SendAsync(
                NeoComposeAuthEndpoints.GetSessionUrl(apiBaseUrl),
                "GET",
                null,
                token.accessToken);

            if (response.IsConnectionError)
            {
                SaveCheckedToken(store, token, nowUnixSeconds);
                return false;
            }

            if (response.StatusCode == 401)
            {
                throw new NeoComposeNotSignedInException(
                    "Your Neo Compose session is no longer valid. Sign in again to continue.");
            }

            if (!response.IsSuccessStatus)
            {
                SaveCheckedToken(store, token, nowUnixSeconds);
                return false;
            }

            var session = TryDeserialize(response.Text);
            var nextAccessToken = response.GetHeader(RefreshedTokenHeader);
            var hasReplacementToken = !string.IsNullOrWhiteSpace(nextAccessToken);
            var nextExpiresAt = ParseSessionTimestamp(session?.session?.expiresAt) ?? token.expiresAtUnixSeconds;
            var nextUpdatedAt = ParseSessionTimestamp(session?.session?.updatedAt) ?? token.updatedAtUnixSeconds;

            if (!hasReplacementToken &&
                nextExpiresAt == token.expiresAtUnixSeconds &&
                nextUpdatedAt == token.updatedAtUnixSeconds)
            {
                SaveCheckedToken(store, token, nowUnixSeconds);
                return false;
            }

            store.Save(new NeoComposeStoredToken(
                hasReplacementToken ? nextAccessToken! : token.accessToken,
                nextExpiresAt,
                nextUpdatedAt,
                nowUnixSeconds,
                token.scopes,
                token.authBaseUrl,
                session?.user?.name ?? token.displayName,
                session?.user?.email ?? token.displayEmail));
            return hasReplacementToken;
        }

        private static void SaveCheckedToken(
            INeoComposeTokenStore store,
            NeoComposeStoredToken token,
            long checkedAtUnixSeconds)
        {
            store.Save(new NeoComposeStoredToken(
                token.accessToken,
                token.expiresAtUnixSeconds,
                token.updatedAtUnixSeconds,
                checkedAtUnixSeconds,
                token.scopes,
                token.authBaseUrl,
                token.displayName,
                token.displayEmail));
        }

        private static NeoComposeSessionResponse? TryDeserialize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonConvert.DeserializeObject<NeoComposeSessionResponse>(text);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static long? ParseSessionTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value!.Trim();
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return NormalizeEpoch(integer);
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return NormalizeEpoch((long)Math.Round(number));
            }

            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                return date.ToUnixTimeSeconds();
            }

            return null;
        }

        private static long NormalizeEpoch(long value)
        {
            return value > 10_000_000_000L ? value / 1000L : value;
        }
    }
}
