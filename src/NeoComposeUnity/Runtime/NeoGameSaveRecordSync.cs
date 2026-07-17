// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Incrementally assembles a save overlay from payload-free descriptors
    /// and cached record states. Runtime reads are all-partition by design;
    /// the web's partition-scoped functions are a separate server contract.
    /// </summary>
    internal static class NeoGameSaveRecordSync
    {
        internal const int DefaultPageSize = 128;

        public static async Awaitable LoadManifestAsync(
            INeoApiClient api,
            string customId,
            RemoteGameSave save,
            GameSaveRecordCache? reusableCache = null)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (save == null) throw new ArgumentNullException(nameof(save));

            var cache = reusableCache ?? save.recordCache;
            cache.ResetManifest(save.snapshotId);
            var values = new JObject();
            var staticBindings = new Dictionary<string, string?>();
            string? cursor = null;
            do
            {
                var page = await api.GetSaveRecordManifestPageAsync(
                    customId,
                    save.snapshotId,
                    new GameSaveRecordPageRequest
                    {
                        cursor = cursor,
                        numItems = DefaultPageSize,
                    });
                await FetchAndApplyPageAsync(
                    api, customId, save.snapshotId, cache, page.page, values, staticBindings);
                cursor = NextCursor(cursor, page);
            }
            while (cursor != null);

            cache.snapshotRevision = save.snapshotRevision;
            save.recordCache = cache;
            save.values = new NeoSaveValues(values);
            save.staticBindings = staticBindings;
            // The cloud record model has no partition blobs. The merged local
            // artifact still carries mapKey stamps rehydrated on its value rows.
            save.valuePartitions = null;
        }

        public static async Awaitable<bool> ApplyDeltaAsync(
            INeoApiClient api,
            string customId,
            LocalGameSave save,
            long throughRevision)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (string.IsNullOrEmpty(save.snapshotId)) return false;
            if (throughRevision <= save.snapshotRevision) return false;

            var values = save.values.Raw is JObject current
                ? (JObject)current.DeepClone()
                : new JObject();
            var staticBindings = new Dictionary<string, string?>(save.staticBindings);
            string? cursor = null;
            do
            {
                var page = await api.GetSaveRecordDeltaPageAsync(
                    customId,
                    save.snapshotId!,
                    new GameSaveRecordDeltaPageRequest
                    {
                        afterRevision = save.snapshotRevision,
                        throughRevision = throughRevision,
                        cursor = cursor,
                        numItems = DefaultPageSize,
                    });
                await FetchAndApplyPageAsync(
                    api,
                    customId,
                    save.snapshotId!,
                    save.recordCache,
                    page.page,
                    values,
                    staticBindings);
                cursor = NextCursor(cursor, page);
            }
            while (cursor != null);

            // Advance only after every bounded page and state batch succeeds.
            save.snapshotRevision = throughRevision;
            save.recordCache.snapshotId = save.snapshotId;
            save.recordCache.snapshotRevision = throughRevision;
            save.values = new NeoSaveValues(values);
            save.staticBindings = staticBindings;
            save.valuePartitions = null;
            return true;
        }

        private static async Awaitable FetchAndApplyPageAsync(
            INeoApiClient api,
            string customId,
            string snapshotId,
            GameSaveRecordCache cache,
            List<GameSaveRecordDescriptor> descriptors,
            JObject values,
            IDictionary<string, string?> staticBindings)
        {
            var missingIds = cache.FindMissingStateIds(descriptors);
            if (missingIds.Count != 0)
            {
                var states = await api.GetSaveRecordStatesAsync(
                    customId, snapshotId, missingIds);
                cache.StoreStates(descriptors, states);
            }
            cache.ApplyDescriptors(descriptors, values, staticBindings);
        }

        private static string? NextCursor(string? previous, GameSaveRecordPage page)
        {
            if (page.isDone) return null;
            if (string.IsNullOrEmpty(page.continueCursor))
            {
                throw new InvalidOperationException(
                    "Save record page was not done but omitted its continuation cursor.");
            }
            if (page.continueCursor == previous)
            {
                throw new InvalidOperationException(
                    "Save record page repeated its continuation cursor.");
            }
            return page.continueCursor;
        }
    }
}
