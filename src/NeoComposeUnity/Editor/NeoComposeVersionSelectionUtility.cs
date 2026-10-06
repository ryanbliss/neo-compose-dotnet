// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace NeoCompose.Unity.Editor
{
    public static class NeoComposeVersionSelectionUtility
    {
        /// <summary>
        /// Branches display by name: their semver is a fork-time placeholder
        /// that collides with real releases.
        /// </summary>
        public static string DisplayLabel(NeoComposeProjectVersion version)
        {
            var label = version.kind == "branch" && !string.IsNullOrWhiteSpace(version.name)
                ? version.name!
                : version.semver.label;
            return string.IsNullOrWhiteSpace(label) ? version.id : label;
        }

        public static string SelectDefaultReleaseChannelId(
            IEnumerable<NeoComposeProjectReleaseChannel> channels)
        {
            var ordered = OrderChannels(channels).ToArray();
            var development = ordered.FirstOrDefault(channel =>
                string.Equals(channel.id, "development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.slug, "development", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.name, "Development", StringComparison.OrdinalIgnoreCase));
            return (development ?? ordered.FirstOrDefault())?.id ?? "";
        }

        public static bool IsVersionInChannel(
            NeoComposeProjectVersion version,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            string channelId)
        {
            var status = FindStatus(version, statuses);
            return status?.releaseChannelIds.Contains(channelId) ?? false;
        }

        public static bool IsCurrentVersionWritable(
            string versionId,
            IEnumerable<NeoComposeProjectVersion> versions,
            IEnumerable<NeoComposeProjectVersionStatus> statuses)
        {
            var version = versions.FirstOrDefault(candidate => candidate.id == versionId);
            if (version == null)
                return false;
            return FindStatus(version, statuses)?.isWritable ?? false;
        }

        public static bool IsArchived(NeoComposeProjectVersion version)
        {
            return !string.IsNullOrWhiteSpace(version.archivedAt);
        }

        public static bool IsDeprecated(
            NeoComposeProjectVersion version,
            IEnumerable<NeoComposeProjectVersionStatus> statuses)
        {
            var status = FindStatus(version, statuses);
            if (status == null)
                return false;
            if (string.Equals(status.name, "Deprecated", StringComparison.OrdinalIgnoreCase))
                return true;
            return status.releaseChannelIds.Length == 0;
        }

        public static string[] GetTargetReleaseChannelNames(
            NeoComposeProjectVersion version,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            IEnumerable<NeoComposeProjectReleaseChannel> channels)
        {
            var status = FindStatus(version, statuses);
            if (status == null)
                return Array.Empty<string>();
            var channelsById = channels.ToDictionary(channel => channel.id, channel => channel.name);
            return status.releaseChannelIds
                .Select(channelId => channelsById.TryGetValue(channelId, out var name) ? name : channelId)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }

        public static NeoComposeProjectVersionStatus? FindStatus(
            NeoComposeProjectVersion version,
            IEnumerable<NeoComposeProjectVersionStatus> statuses)
        {
            return statuses.FirstOrDefault(status => status.id == version.statusId);
        }

        public static IEnumerable<NeoComposeProjectReleaseChannel> OrderChannels(
            IEnumerable<NeoComposeProjectReleaseChannel> channels)
        {
            return channels
                .OrderBy(channel => channel.sortOrder)
                .ThenBy(channel => channel.name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
