// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal static class NeoChangeListenerPatches
    {
        internal const int MaxRecordBytes = 1_048_576;

        // Convex's current opaque ids use 31-32 ASCII bytes. Reserve 64, matching
        // the server's capacity guard, so its typical 32-byte estimate cannot
        // undercount a longer system id. Application ids use their actual size;
        // not-yet-assigned state/save/snapshot ids are generated UUIDs (36 bytes).
        internal const int SystemIdByteLimit = 64;
        private const int ConvexNumberBytes = 9; // Type marker + IEEE-754 Float64.
        private const int ConvexNullBytes = 1; // Null type marker.

        internal static int EncodedRecordSize(ProjectSaveData save, string rootId, NeoChangeListenerMap map,
            int rootMemberIdByteLimit)
        {
            const string pendingId = "00000000-0000-0000-0000-000000000000";
            int bytes = 2 + 6 + SystemIdByteLimit + 14 + ConvexNumberBytes;
            bytes += StringField("id", pendingId);
            bytes += StringField("projectId", save.projectId);
            bytes += StringField("sourceGameSaveId", save.serverId ?? pendingId);
            bytes += StringField("versionId", save.version?.id ?? pendingId);
            bytes += StringField("recordKind", "change-listeners");
            bytes += StringField("recordId", rootId);
            bytes += StringField("mutableOwnerSnapshotId", save.snapshotId ?? pendingId);
            bytes += "dataJson".Length + 1 + 2 + EncodedMapSize(map);
            foreach (string name in new[] { "dataSchemaVersion", "createdAt", "updatedAt" })
                bytes += Encoding.UTF8.GetByteCount(name) + 1 + ConvexNumberBytes;
            foreach (string name in new[] { "valueMemberId", "valueClassId", "valueContainerId", "staticBindingMemberId" })
                bytes += Encoding.UTF8.GetByteCount(name) + 1 + ConvexNullBytes;
            // The root projection is a declared member id. The caller supplies
            // the largest UTF-8 member-id length in this schema, including roots
            // that the pending plan has not published yet. This bounds null too.
            bytes += "valueRootMemberId".Length + 1 + 2 + rootMemberIdByteLimit;
            return bytes;
        }

        private static int EncodedMapSize(NeoChangeListenerMap map)
        {
            int bytes = "{\"changeListeners\":{}}".Length;
            bool firstOwner = true;
            foreach (var owner in map)
            {
                if (!firstOwner)
                    bytes++;
                firstOwner = false;
                bytes += JsonStringSize(owner.Key) + 3; // Key, colon, object braces.
                bool firstMember = true;
                foreach (var member in owner.Value)
                {
                    if (!firstMember)
                        bytes++;
                    firstMember = false;
                    bytes += JsonStringSize(member.Key) + 3; // Key, colon, array brackets.
                    for (int index = 0; index < member.Value.Length; index++)
                    {
                        if (index != 0)
                            bytes++;
                        var target = member.Value[index];
                        bytes += "{\"memberId\":,\"valueId\":}".Length
                            + JsonStringSize(target.memberId) + JsonStringSize(target.valueId);
                    }
                }
            }
            return bytes;
        }

        // UTF-8 size of JSON.stringify's well-formed string spelling. Newtonsoft
        // escapes additional Unicode characters, so its byte count is not parity.
        private static int JsonStringSize(string? value)
        {
            if (value is null)
                return 4;
            int bytes = 2;
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (c is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t')
                    bytes += 2;
                else if (c < 32)
                    bytes += 6;
                else if (c < 128)
                    bytes++;
                else if (c < 2048)
                    bytes += 2;
                else if (char.IsHighSurrogate(c) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    bytes += 4;
                    index++;
                }
                else
                    bytes += char.IsSurrogate(c) ? 6 : 3;
            }
            return bytes;
        }

        internal static bool TargetsEqual(NeoDelegateValue[]? left, NeoDelegateValue[]? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index].memberId != right[index].memberId || left[index].valueId != right[index].valueId)
                    return false;
            return true;
        }

        private static int StringField(string name, string value) =>
            Encoding.UTF8.GetByteCount(name) + 1 + 2 + Encoding.UTF8.GetByteCount(value);

        internal static Dictionary<string, NeoChangeListenerMap> Copy(IReadOnlyDictionary<string, NeoChangeListenerMap>? source) =>
            source is null ? new(StringComparer.Ordinal) : new(source, StringComparer.Ordinal);

        internal static void AppendChanges(NeoSavePatch patch,
            IReadOnlyDictionary<string, NeoChangeListenerMap>? baseline,
            IReadOnlyDictionary<string, NeoChangeListenerMap>? staged,
            GameSaveRecordCache? cache,
            IReadOnlyDictionary<string, HashSet<string>>? dirtyOwners = null,
            IReadOnlyDictionary<string, GameSaveListenerEndpointLocator>? endpoints = null)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            if (dirtyOwners is not null)
                roots.UnionWith(dirtyOwners.Keys);
            else
            {
                if (baseline is not null)
                    roots.UnionWith(baseline.Keys);
                if (staged is not null)
                    roots.UnionWith(staged.Keys);
            }
            foreach (var root in roots)
            {
                var before = baseline?.GetValueOrDefault(root);
                var after = staged?.GetValueOrDefault(root);
                var owners = new HashSet<string>(StringComparer.Ordinal);
                if (dirtyOwners is not null)
                    owners.UnionWith(dirtyOwners[root]);
                else
                {
                    if (before is not null)
                        owners.UnionWith(before.Keys);
                    if (after is not null)
                        owners.UnionWith(after.Keys);
                }
                var change = new GameSaveChangeListenersPatchChange { rootId = root };
                if (cache?.descriptors.TryGetValue(GameSaveRecordDescriptor.MakeLogicalKey(
                    NeoGameSaveRecordKinds.ChangeListeners, root), out var descriptor) == true && !descriptor.deleted)
                {
                    change.baseRecordStateId = descriptor.recordStateId;
                    change.baseRecordRevisionToken = descriptor.recordRevisionToken;
                }
                foreach (var owner in owners)
                {
                    var previous = before?.GetValueOrDefault(owner);
                    var next = after?.GetValueOrDefault(owner);
                    if (EntryEqual(previous, next))
                        continue;
                    change.edits.Add(new GameSaveListenerOwnerEdit
                    {
                        ownerId = owner,
                        expected = CopyEntry(previous),
                        entry = CopyEntry(next),
                    });
                }
                if (change.edits.Count > 0)
                {
                    foreach (string id in RequiredEndpointIds(change.edits))
                    {
                        if (endpoints is null || !endpoints.TryGetValue(id, out var endpoint))
                            throw new InvalidOperationException($"Listener endpoint '{id}' has no owning path in this save capture. Load this exact save through NeoClient and call CommitAsync(forceCapture: true) to capture listener paths before synchronizing additions.");
                        change.endpoints.Add(endpoint);
                    }
                    patch.changes.Add(change);
                }
            }
        }

        internal static HashSet<string> RequiredEndpointIds(IEnumerable<GameSaveListenerOwnerEdit> edits)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                if (edit.entry is null)
                    continue;
                foreach (var slot in edit.entry)
                {
                    NeoDelegateValue[]? previous = edit.expected?.GetValueOrDefault(slot.Key);
                    var known = new HashSet<(string? memberId, string? valueId)>();
                    if (previous is not null)
                        foreach (var target in previous)
                            known.Add((target.memberId, target.valueId));
                    if (previous is null)
                        ids.Add(edit.ownerId);
                    foreach (var target in slot.Value)
                    {
                        if (known.Contains((target.memberId, target.valueId)))
                            continue;
                        ids.Add(edit.ownerId);
                        if (target.valueId is not null)
                            ids.Add(target.valueId);
                    }
                }
            }
            return ids;
        }

        private static bool EntryEqual(Dictionary<string, NeoDelegateValue[]>? left, Dictionary<string, NeoDelegateValue[]>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var values) || !TargetsEqual(values, pair.Value))
                    return false;
            }
            return true;
        }

        private static Dictionary<string, NeoDelegateValue[]>? CopyEntry(Dictionary<string, NeoDelegateValue[]>? source)
        {
            if (source is null)
                return null;
            var copy = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal);
            foreach (var pair in source)
                copy[pair.Key] = Array.ConvertAll(pair.Value, target => target.PersistedCopy());
            return copy;
        }

        internal static void Apply(IDictionary<string, NeoChangeListenerMap> maps, NeoSavePatch patch, bool expected = false)
        {
            foreach (var operation in patch.changes)
            {
                if (operation is not GameSaveChangeListenersPatchChange change)
                    continue;
                var map = maps.TryGetValue(change.rootId, out var previous) ? previous.Copy() : new NeoChangeListenerMap();
                foreach (var edit in change.edits)
                {
                    var entry = expected ? edit.expected : edit.entry;
                    if (entry is null || entry.Count == 0)
                        map.Remove(edit.ownerId);
                    else
                        map[edit.ownerId] = CopyEntry(entry)!;
                }
                if (map.Count == 0)
                    maps.Remove(change.rootId);
                else
                    maps[change.rootId] = map;
            }
        }
    }
}
