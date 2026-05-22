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

        public static NeoComposeProjectVersion? SelectLatestVersionForChannel(
            IEnumerable<NeoComposeProjectVersion> versions,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            string channelId)
        {
            return VersionsForChannel(versions, statuses, channelId)
                .Where(version => !IsArchived(version))
                .OrderByDescending(version => version, SemverComparer.Instance)
                .FirstOrDefault();
        }

        public static NeoComposeProjectVersion[] BuildVersionDropdownOptions(
            IEnumerable<NeoComposeProjectVersion> versions,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            string channelId,
            string currentVersionId)
        {
            var options = VersionsForChannel(versions, statuses, channelId)
                .Where(version => !IsArchived(version))
                .OrderByDescending(version => version, SemverComparer.Instance)
                .ToList();

            if (!string.IsNullOrWhiteSpace(currentVersionId) &&
                options.All(version => version.id != currentVersionId))
            {
                var current = versions.FirstOrDefault(version => version.id == currentVersionId);
                if (current != null)
                {
                    options.Insert(0, current);
                }
            }

            return options.ToArray();
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
            if (version == null) return false;
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
            if (status == null) return false;
            if (string.Equals(status.name, "Deprecated", StringComparison.OrdinalIgnoreCase)) return true;
            return status.releaseChannelIds.Length == 0;
        }

        public static string[] GetTargetReleaseChannelNames(
            NeoComposeProjectVersion version,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            IEnumerable<NeoComposeProjectReleaseChannel> channels)
        {
            var status = FindStatus(version, statuses);
            if (status == null) return Array.Empty<string>();
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

        public static int CompareSemver(
            NeoComposeProjectVersion? lhs,
            NeoComposeProjectVersion? rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return 0;
            if (lhs == null) return -1;
            if (rhs == null) return 1;
            var major = lhs.semver.major.CompareTo(rhs.semver.major);
            if (major != 0) return major;
            var minor = lhs.semver.minor.CompareTo(rhs.semver.minor);
            if (minor != 0) return minor;
            return lhs.semver.patch.CompareTo(rhs.semver.patch);
        }

        private static IEnumerable<NeoComposeProjectVersion> VersionsForChannel(
            IEnumerable<NeoComposeProjectVersion> versions,
            IEnumerable<NeoComposeProjectVersionStatus> statuses,
            string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return Array.Empty<NeoComposeProjectVersion>();
            return versions.Where(version => IsVersionInChannel(version, statuses, channelId));
        }

        private sealed class SemverComparer : IComparer<NeoComposeProjectVersion>
        {
            public static readonly SemverComparer Instance = new();

            public int Compare(NeoComposeProjectVersion? x, NeoComposeProjectVersion? y)
            {
                return CompareSemver(x, y);
            }
        }
    }
}
