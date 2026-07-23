// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public static class NeoGameSaveRecordKinds
    {
        public const string Value = "value";
        public const string StaticBinding = "static-binding";
    }

    public static class NeoGameSaveRecordChangeKinds
    {
        public const string ValuePatch = "value.patch";
        public const string ValueReplace = "value.replace";
        public const string ValueRestoreToAuthored = "value.restore-to-authored";
        public const string StaticBindingSet = "static-binding.set";
        public const string StaticBindingRestoreToAuthored =
            "static-binding.restore-to-authored";
    }

    /// <summary>
    /// One record-scoped live-save write. Concrete variants mirror the
    /// server's discriminated <c>changes</c> union and carry their own OCC base.
    /// </summary>
    [JsonConverter(typeof(NeoGameSaveRecordChangeConverter))]
    public abstract class GameSaveRecordChange
    {
        public abstract string kind { get; }
    }

    public abstract class BasedGameSaveRecordChange : GameSaveRecordChange
    {
        public string baseRecordStateId = "";
        public string baseRecordRevisionToken = "";
    }

    public sealed class GameSaveValuePatchChange : BasedGameSaveRecordChange
    {
        public override string kind => NeoGameSaveRecordChangeKinds.ValuePatch;
        public string valueId = "";
        public Dictionary<string, JToken> set = new();
        public List<string> unset = new();
    }

    public sealed class GameSaveValueReplaceChange : GameSaveRecordChange
    {
        public override string kind => NeoGameSaveRecordChangeKinds.ValueReplace;
        public string valueId = "";
        public string? baseRecordStateId;
        public string? baseRecordRevisionToken;
        public JToken value = JValue.CreateNull();
    }

    public sealed class GameSaveValueRestoreToAuthoredChange : BasedGameSaveRecordChange
    {
        public override string kind => NeoGameSaveRecordChangeKinds.ValueRestoreToAuthored;
        public string valueId = "";
    }

    public sealed class GameSaveStaticBindingSetChange : GameSaveRecordChange
    {
        public override string kind => NeoGameSaveRecordChangeKinds.StaticBindingSet;
        public string memberId = "";
        public string? baseRecordStateId;
        public string? baseRecordRevisionToken;
        public string? valueId;
    }

    public sealed class GameSaveStaticBindingRestoreToAuthoredChange
        : BasedGameSaveRecordChange
    {
        public override string kind =>
            NeoGameSaveRecordChangeKinds.StaticBindingRestoreToAuthored;
        public string memberId = "";
    }

    public sealed class NeoGameSaveRecordChangeConverter
        : DiscriminatedConverter<GameSaveRecordChange>
    {
        protected override string DiscriminatorField => "kind";

        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case NeoGameSaveRecordChangeKinds.ValuePatch:
                    return typeof(GameSaveValuePatchChange);
                case NeoGameSaveRecordChangeKinds.ValueReplace:
                    return typeof(GameSaveValueReplaceChange);
                case NeoGameSaveRecordChangeKinds.ValueRestoreToAuthored:
                    return typeof(GameSaveValueRestoreToAuthoredChange);
                case NeoGameSaveRecordChangeKinds.StaticBindingSet:
                    return typeof(GameSaveStaticBindingSetChange);
                case NeoGameSaveRecordChangeKinds.StaticBindingRestoreToAuthored:
                    return typeof(GameSaveStaticBindingRestoreToAuthoredChange);
                default:
                    return null;
            }
        }
    }

    /// <summary>Payload-free current descriptor for one save override record.</summary>
    public sealed class GameSaveRecordDescriptor
    {
        public string recordKind = "";
        public string recordId = "";
        public string? mapKey;
        public string? recordStateId;
        public string? recordRevisionToken;
        public string? contentHash;
        public bool deleted;
        public long lastChangedRevision;

        [JsonIgnore]
        public string LogicalKey => MakeLogicalKey(recordKind, recordId);

        [JsonIgnore]
        public string? StateCacheKey => deleted
            || recordStateId == null
            || recordRevisionToken == null
            || contentHash == null
                ? null
                : MakeStateCacheKey(recordStateId, recordRevisionToken, contentHash);

        public bool MatchesPayload(GameSaveRecordDescriptor other) =>
            other != null
            && recordStateId == other.recordStateId
            && recordRevisionToken == other.recordRevisionToken
            && contentHash == other.contentHash;

        public static string MakeLogicalKey(string recordKind, string recordId) =>
            recordKind + ":" + recordId;

        public static string MakeStateCacheKey(
            string recordStateId,
            string recordRevisionToken,
            string contentHash) =>
            recordStateId + ":" + recordRevisionToken + ":" + contentHash;
    }

    /// <summary>
    /// Complete record payload selected by a descriptor. Identity and routing
    /// remain canonical on the descriptor and are intentionally absent here.
    /// </summary>
    public sealed class GameSaveRecordState
    {
        public string id = "";
        public string recordKind = "";
        public string recordId = "";
        public int dataSchemaVersion;
        public string dataJson = "";
    }

    public sealed class GameSaveRecordPage
    {
        public List<GameSaveRecordDescriptor> page = new();
        public bool isDone;
        public string? continueCursor;
    }

    public class GameSaveRecordPageRequest
    {
        public string? cursor;
        public int numItems = 128;
    }

    public sealed class GameSaveRecordDeltaPageRequest : GameSaveRecordPageRequest
    {
        public long afterRevision;
        public long throughRevision;
    }

    public sealed class GameSaveRecordStatesRequest
    {
        public List<string> recordStateIds = new();
    }

    public sealed class GameSaveRecordStatesResponse
    {
        public List<GameSaveRecordState> states = new();
    }

    /// <summary>The only payload carried by the live save-head subscription.</summary>
    public sealed class GameSaveSnapshotRevisionSignal
    {
        public string snapshotId = "";
        public long snapshotRevision;
    }

    public sealed class GameSaveRecordConflict
    {
        public string recordKind = "";
        public string recordId = "";
        public string? expectedRecordStateId;
        public string? currentRecordStateId;
        public string? expectedRecordRevisionToken;
        public string? currentRecordRevisionToken;
        public GameSaveRecordDescriptor currentDescriptor = new();
    }

    /// <summary>
    /// Persistable descriptor + state cache used by manifest and delta loads.
    /// State cache keys include the revision token so a live-owned state ID
    /// changing in place can never reuse stale payload.
    /// </summary>
    public sealed class GameSaveRecordCache
    {
        public string? snapshotId;
        public long snapshotRevision;
        public Dictionary<string, GameSaveRecordDescriptor> descriptors = new();
        public Dictionary<string, GameSaveRecordState> states = new();

        public void ResetManifest(string nextSnapshotId)
        {
            snapshotId = nextSnapshotId;
            snapshotRevision = 0;
            descriptors.Clear();
            // Keep payloads: copied descriptors in a new snapshot commonly
            // select the same immutable state and can reuse them immediately.
        }

        public List<string> FindMissingStateIds(
            IEnumerable<GameSaveRecordDescriptor> incoming)
        {
            var missing = new List<string>();
            var seen = new HashSet<string>();
            foreach (var descriptor in incoming)
            {
                var cacheKey = descriptor.StateCacheKey;
                var stateId = descriptor.recordStateId;
                if (cacheKey == null || stateId == null || states.ContainsKey(cacheKey)) continue;
                if (seen.Add(stateId)) missing.Add(stateId);
            }
            return missing;
        }

        public void StoreStates(
            IEnumerable<GameSaveRecordDescriptor> incoming,
            IEnumerable<GameSaveRecordState> fetched)
        {
            var byId = new Dictionary<string, GameSaveRecordState>();
            foreach (var state in fetched) byId[state.id] = state;
            foreach (var descriptor in incoming)
            {
                var cacheKey = descriptor.StateCacheKey;
                if (cacheKey == null || descriptor.recordStateId == null) continue;
                if (byId.TryGetValue(descriptor.recordStateId, out var state))
                {
                    var obsoleteKeys = new List<string>();
                    foreach (var cached in states)
                    {
                        if (cached.Value.id == state.id && cached.Key != cacheKey)
                        {
                            obsoleteKeys.Add(cached.Key);
                        }
                    }
                    foreach (var obsoleteKey in obsoleteKeys) states.Remove(obsoleteKey);
                    states[cacheKey] = state;
                }
            }
        }

        public void ApplyDescriptors(
            IEnumerable<GameSaveRecordDescriptor> incoming,
            JObject values,
            IDictionary<string, string?> staticBindings)
        {
            foreach (var descriptor in incoming)
            {
                descriptors[descriptor.LogicalKey] = descriptor;
                if (descriptor.recordKind == NeoGameSaveRecordKinds.Value)
                {
                    if (descriptor.deleted)
                    {
                        values.Remove(descriptor.recordId);
                        continue;
                    }

                    var state = RequireCachedState(descriptor);
                    var data = ParseDataObject(state, descriptor);
                    data["id"] = descriptor.recordId;
                    if (descriptor.mapKey == null) data.Remove("mapKey");
                    else data["mapKey"] = descriptor.mapKey;
                    values[descriptor.recordId] = data;
                    continue;
                }

                if (descriptor.recordKind == NeoGameSaveRecordKinds.StaticBinding)
                {
                    if (descriptor.deleted)
                    {
                        staticBindings.Remove(descriptor.recordId);
                        continue;
                    }

                    var state = RequireCachedState(descriptor);
                    var data = ParseDataObject(state, descriptor);
                    var valueId = data["valueId"];
                    if (valueId == null || valueId.Type == JTokenType.Null)
                    {
                        staticBindings[descriptor.recordId] = null;
                    }
                    else if (valueId.Type == JTokenType.String)
                    {
                        staticBindings[descriptor.recordId] = valueId.Value<string>();
                    }
                    else
                    {
                        throw new JsonSerializationException(
                            $"Save static-binding record '{descriptor.recordId}' has a non-string valueId.");
                    }
                    continue;
                }

                throw new JsonSerializationException(
                    $"Unknown save record kind '{descriptor.recordKind}'.");
            }
        }

        private GameSaveRecordState RequireCachedState(GameSaveRecordDescriptor descriptor)
        {
            var cacheKey = descriptor.StateCacheKey;
            if (cacheKey == null || !states.TryGetValue(cacheKey, out var state))
            {
                throw new InvalidOperationException(
                    $"Save record '{descriptor.LogicalKey}' has no cached payload for its current descriptor.");
            }
            if (state.recordKind != descriptor.recordKind || state.recordId != descriptor.recordId)
            {
                throw new JsonSerializationException(
                    $"Save state '{state.id}' identity does not match descriptor '{descriptor.LogicalKey}'.");
            }
            return state;
        }

        private static JObject ParseDataObject(
            GameSaveRecordState state,
            GameSaveRecordDescriptor descriptor)
        {
            JToken parsed;
            try
            {
                parsed = JToken.Parse(state.dataJson);
            }
            catch (JsonException exception)
            {
                throw new JsonSerializationException(
                    $"Save record '{descriptor.LogicalKey}' contains invalid dataJson.", exception);
            }
            if (parsed is not JObject obj)
            {
                throw new JsonSerializationException(
                    $"Save record '{descriptor.LogicalKey}' dataJson must be an object.");
            }
            if (obj["id"] != null || obj["mapKey"] != null)
            {
                throw new JsonSerializationException(
                    $"Save record '{descriptor.LogicalKey}' dataJson duplicated canonical head fields.");
            }
            return obj;
        }
    }
}
