// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// P76 §5 — the reader half of packed subtree storage. The C# twin of
    /// <c>src/models/members/packed-value-encoding.ts</c>; the SDK decodes and
    /// never encodes, because it does not author project records.
    ///
    /// <para>A <c>.Sparse</c> child occupies a position in its parent's stored
    /// content as a plain value-id string. A <c>.Packed</c> child occupies the
    /// same position as <c>{"~packed": {…}}</c> and has no row of its own: its
    /// content — and its whole materialized subtree — is stored inside the
    /// parent document. Export schema 31 ships that PHYSICAL row set, so a
    /// packed child is absent from <c>values</c> entirely.</para>
    ///
    /// <para>Expansion is the shared read boundary and runs exactly once, where
    /// a row set is assembled (<see cref="ProjectDataConverter"/>), never per
    /// read. Everything downstream — <c>NeoClient.TryGetValue</c>, member
    /// nodes, lists, the evaluator, ownership walks — then sees precisely the
    /// rows a <c>.Sparse</c> corpus would have handed it. A packed child is a
    /// durable authored row that happens to be stored elsewhere, not a P75
    /// virtual replay, so it lands in the same map as every other authored
    /// row.</para>
    ///
    /// <para>Schema-free by construction: the envelope is self-describing, so
    /// decoding needs no class or member context and cannot disagree with the
    /// web about which rows exist.</para>
    /// </summary>
    public static class NeoPackedValue
    {
        /// <summary>
        /// The single discriminating wire key, <c>"~packed"</c>.
        ///
        /// <para>The <c>~</c> prefix carries the same two loads it carries for
        /// <see cref="NeoPartialLeafValue.EnvelopeKey"/>, for the same reasons.
        /// No author can write it — <c>~</c> is not an identifier character in
        /// NeoScript or <c>.neo</c>, so it collides with no schema key,
        /// dictionary key, or structured-leaf field key. And it is not
        /// <c>$</c>, which Convex reserves in every serialized position, so a
        /// <c>$packed</c> spelling would be constructible in memory and unable
        /// to survive a round trip.</para>
        ///
        /// <para>The key is a wire disambiguator, never a path segment: an
        /// override path, a search path, and a derived-edge path all address
        /// the child's own schema key, and the envelope is transparent to
        /// every one of them.</para>
        /// </summary>
        public const string EnvelopeKey = "~packed";

        /// <summary>
        /// Row fields a packed entry never stores, because decoding reproduces
        /// them from the owning row. Storing one is a malformed payload rather
        /// than a tolerable redundancy: a differing value could not be
        /// represented, only contradicted.
        ///
        /// <para><c>projectId</c> — a packed child lives inside its parent's
        /// document and is in its parent's project by construction.
        /// <c>containerId</c> — set iff the row is an entry of an UNORDERED
        /// list, whose membership is "the set of live rows carrying the list's
        /// id here"; a packed child has no row, so it cannot belong to that
        /// set. <c>mapKey</c> — the storage partition is a serialization
        /// concept and a packed child is serialized inside its parent, so it
        /// inherits the parent's partition.</para>
        ///
        /// <para><c>createdAt</c>/<c>updatedAt</c> are deliberately NOT here: a
        /// packed child untouched by a later parent write keeps its own older
        /// <c>updatedAt</c>, so neither timestamp is derivable and both are
        /// required.</para>
        /// </summary>
        private static readonly string[] ForbiddenEntryFields =
        {
            "projectId",
            "containerId",
            "mapKey",
        };

        private static readonly string[] InstanceProvenanceFields =
        {
            "instanceConstructorId",
            "instanceVariantId",
            "instanceVariantRowValueId",
        };

        /// <summary>
        /// True when <paramref name="token"/> is a packed-child envelope.
        ///
        /// <para>Deliberately just "an object carrying the key", with none of
        /// the looseness <see cref="NeoPartialLeafValue.IsEnvelope"/> needs:
        /// this probe only ever runs against a top-level ENTRY of a parent's
        /// content, and those entries are child-id strings under every member
        /// kind that owns children. An object there is already not a child id,
        /// so there is nothing for the probe to be ambiguous against.</para>
        /// </summary>
        internal static bool IsEnvelope(JToken? token) =>
            token is JObject envelope && envelope.Property(EnvelopeKey) is not null;

        /// <summary>
        /// True when any top-level entry of <paramref name="container"/> is a
        /// packed envelope. Nothing deeper is a containment position: below the
        /// top level a value body holds structured-leaf fields, listener
        /// records, or closure payloads, none of which own rows.
        /// </summary>
        internal static bool ContainerHoldsEntry(JToken? container)
        {
            if (container is JArray array)
            {
                foreach (JToken entry in array)
                {
                    if (IsEnvelope(entry)) return true;
                }
                return false;
            }
            if (container is not JObject record) return false;
            foreach (JProperty property in record.Properties())
            {
                if (IsEnvelope(property.Value)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this row stores any packed child at all — the identity fast
        /// path every read seam depends on. A corpus with no packed member
        /// answers false for every row, so expansion costs one shallow scan and
        /// hands the caller back its own rows.
        /// </summary>
        internal static bool RowCarriesPackedContent(JObject row) =>
            ContainerHoldsEntry(row["value"])
            || ContainerHoldsEntry(row["constructorArgs"]);

        /// <summary>
        /// Expands a physically stored <c>{id: row}</c> map into the logical
        /// map every reader downstream expects: each packed parent restored to
        /// the row it would have stored under <c>.Sparse</c>, plus one entry
        /// per packed child, keyed by its logical id.
        ///
        /// <para>Returns <paramref name="values"/> itself when nothing is
        /// packed, so the boundary costs one shallow scan until a packed member
        /// exists.</para>
        ///
        /// <para><paramref name="context"/> names the row set (the export's
        /// <c>values</c>, or a storage partition) for collision errors.</para>
        /// </summary>
        internal static JObject Expand(JObject values, string context)
        {
            bool carriesPacked = false;
            foreach (JProperty property in values.Properties())
            {
                if (property.Value is not JObject row) continue;
                if (!RowCarriesPackedContent(row)) continue;
                carriesPacked = true;
                break;
            }
            if (!carriesPacked) return values;

            var expanded = new JObject();
            foreach (JProperty property in values.Properties())
            {
                if (property.Value is not JObject row
                    || !RowCarriesPackedContent(row))
                {
                    AddRow(expanded, property.Name, property.Value, context);
                    continue;
                }
                var children = new List<JObject>();
                JObject physical = DecodeRow(row, children);
                AddRow(expanded, property.Name, physical, context);
                foreach (JObject child in children)
                {
                    AddRow(expanded, child.Value<string>("id")!, child, context);
                }
            }
            return expanded;
        }

        private static void AddRow(
            JObject expanded,
            string id,
            JToken row,
            string context)
        {
            if (expanded.Property(id) is not null)
            {
                throw new JsonSerializationException(
                    $"Value id \"{id}\" appears twice in {context} after packed "
                    + "expansion: a packed child cannot share its logical id with "
                    + "another stored row.");
            }
            expanded.Add(id, row);
        }

        /// <summary>
        /// Expands one physically stored row into its logical parent (returned)
        /// plus its logical child rows (appended to
        /// <paramref name="children"/>, parent-first in encounter order).
        /// </summary>
        private static JObject DecodeRow(JObject row, List<JObject> children)
        {
            string rowId = RowId(row);
            var state = new DecodeState(rowId, RowMapKey(row));
            var scope = new Scope(
                $"value \"{rowId}\"",
                IsCollapseStampedInstanceRoot(row) ? rowId : null);
            var decoded = (JObject)row.DeepClone();
            DecodeContentField(decoded["value"], "value", scope, state, children);
            if (IsLiteralValueContent(decoded))
            {
                DecodeContentField(
                    decoded["constructorArgs"],
                    "constructorArgs",
                    scope,
                    state,
                    children);
            }
            return decoded;
        }

        /// <summary>
        /// A child row occupies exactly one TOP-LEVEL entry of its parent's
        /// <c>value</c> (a Class field, a dictionary key, an ordered-list
        /// index) or of its <c>constructorArgs</c> (a settled constructor
        /// argument). Each envelope is replaced in place by its child's id
        /// string, which is what makes the decoded parent byte-identical to the
        /// row the same graph would have stored under <c>.Sparse</c>.
        /// </summary>
        private static void DecodeContentField(
            JToken? container,
            string field,
            Scope scope,
            DecodeState state,
            List<JObject> children)
        {
            if (container is JArray array)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    if (!IsEnvelope(array[index])) continue;
                    array[index] = DecodeEnvelope(
                        (JObject)array[index],
                        $"{field}[{index}]",
                        scope,
                        state,
                        children);
                }
                return;
            }
            if (container is not JObject record) return;
            var properties = new List<JProperty>(record.Properties());
            foreach (JProperty property in properties)
            {
                if (!IsEnvelope(property.Value)) continue;
                property.Value = DecodeEnvelope(
                    (JObject)property.Value,
                    $"{field}[{JsonConvert.ToString(property.Name)}]",
                    scope,
                    state,
                    children);
            }
        }

        /// <summary>Replaces one envelope with its child id, publishing the logical row.</summary>
        private static JToken DecodeEnvelope(
            JObject envelope,
            string position,
            Scope scope,
            DecodeState state,
            List<JObject> children)
        {
            string label = $"Packed value entry at {scope.OwnerLabel}.{position}";
            JObject entry = RequireDecodableEntry(
                envelope.Property(EnvelopeKey)!.Value,
                label);
            string id = DecodedChildId(entry, label, scope);
            if (!state.SeenIds.Add(id))
            {
                throw new JsonSerializationException(
                    $"{label} resolves to id \"{id}\", which another row in the same "
                    + "packed payload already claims.");
            }

            var childScope = new Scope(
                $"packed value \"{id}\"",
                IsCollapseStampedInstanceRoot(entry) ? id : scope.ConstructionRootId);
            var nested = new List<JObject>();
            children.Add(LogicalChildRow(entry, id, childScope, state, nested));
            children.AddRange(nested);
            return new JValue(id);
        }

        /// <summary>
        /// The packed entry restored to a complete stored-row shape: its own id
        /// and its parent's storage partition, with every other stored field
        /// carried verbatim so a field added to the row shape later fails
        /// closed into "stored" rather than being silently dropped.
        /// </summary>
        private static JObject LogicalChildRow(
            JObject entry,
            string id,
            Scope scope,
            DecodeState state,
            List<JObject> children)
        {
            var row = (JObject)entry.DeepClone();
            row["id"] = id;
            if (state.MapKey is not null) row["mapKey"] = state.MapKey;
            if (IsInitValueContent(row)) return row;
            DecodeContentField(row["value"], "value", scope, state, children);
            DecodeContentField(
                row["constructorArgs"],
                "constructorArgs",
                scope,
                state,
                children);
            return row;
        }

        /// <summary>
        /// The logical id of a packed child: derived when its position derives
        /// one, stored otherwise.
        ///
        /// <para>P76 §5 names two canonical id families, and only one of them
        /// can occur inside a packed payload:
        /// <c>VirtualValueId(instanceRootId, sourceIdentity)</c> — a
        /// deterministic non-collection child of an enclosing construction
        /// root, whose inputs are both recoverable from the payload alone. The
        /// member-owned <c>derivedMemberValueId(memberId)</c> family is
        /// unreachable here: that row is referenced from a MEMBER record rather
        /// than from a position inside any value row, and a row with no owning
        /// parent value has nothing to be packed into.</para>
        ///
        /// <para>Everything else — collection entries, independent instance
        /// roots, a child under a parent that is not itself a construction root
        /// — stores its minted id, the same arm current authoring uses.</para>
        ///
        /// <para>REJECTS a derivable id that is nevertheless stored, and a
        /// missing id at a position that derives none. There is no
        /// compatibility branch for either: the P76 canonical-ID migration
        /// rewrites every affected row before any encoder runs, so a payload
        /// that disagrees is corrupt rather than old.</para>
        /// </summary>
        private static string DecodedChildId(JObject entry, string label, Scope scope)
        {
            string? canonical = CanonicalPackedChildId(
                scope.ConstructionRootId,
                entry.Value<string>("sourceValueId"));
            JProperty? stored = entry.Property("id");
            if (stored is null)
            {
                if (canonical is null)
                {
                    throw new JsonSerializationException(
                        $"{label} stores no \"id\" and its position derives none: only "
                        + "a deterministic child of an enclosing construction root, "
                        + "stamped with a \"sourceValueId\", may omit its logical id.");
                }
                return canonical;
            }
            if (stored.Value.Type != JTokenType.String
                || string.IsNullOrEmpty(stored.Value.Value<string>()))
            {
                throw new JsonSerializationException(
                    $"{label} stores an \"id\" that is not a non-empty string.");
            }
            string id = stored.Value.Value<string>()!;
            if (id == canonical)
            {
                throw new JsonSerializationException(
                    $"{label} stores id \"{id}\", which its position already derives; "
                    + "a derivable id must be omitted rather than repeated.");
            }
            return id;
        }

        /// <summary>
        /// The id a packed position reproduces without storing it, or null when
        /// the position has no canonical derivation and must carry an explicit
        /// one.
        /// </summary>
        private static string? CanonicalPackedChildId(
            string? constructionRootId,
            string? sourceValueId)
        {
            if (constructionRootId is null) return null;
            if (string.IsNullOrEmpty(sourceValueId)) return null;
            return NeoClient.VirtualValueId(constructionRootId, sourceValueId!);
        }

        private static JObject RequireDecodableEntry(JToken token, string label)
        {
            if (token is not JObject entry)
            {
                throw new JsonSerializationException($"{label} is not an object.");
            }
            foreach (string field in ForbiddenEntryFields)
            {
                if (entry.Property(field) is null) continue;
                throw new JsonSerializationException(
                    $"{label} stores \"{field}\", which decoding derives from its "
                    + "owning row.");
            }
            if (!IsNumber(entry["createdAt"]))
            {
                throw new JsonSerializationException(
                    $"{label} stores no numeric \"createdAt\".");
            }
            if (!IsNumber(entry["updatedAt"]))
            {
                throw new JsonSerializationException(
                    $"{label} stores no numeric \"updatedAt\".");
            }
            bool hasValue = entry.Property("value") is not null;
            bool hasInit = entry.Property("init") is not null;
            if (hasValue && hasInit)
            {
                throw new JsonSerializationException(
                    $"{label} stores both \"value\" and \"init\"; a value container "
                    + "holds exactly one.");
            }
            if (!hasValue && !hasInit)
            {
                throw new JsonSerializationException(
                    $"{label} stores neither \"value\" nor \"init\"; a value container "
                    + "holds exactly one.");
            }
            if (hasInit && !IsInitValueContent(entry))
            {
                throw new JsonSerializationException(
                    $"{label} stores an \"init\" that is not a valid initializer body.");
            }
            if (hasValue && !IsLiteralValueContent(entry))
            {
                throw new JsonSerializationException(
                    $"{label} stores a \"value\" whose class or construction stamp is "
                    + "malformed.");
            }
            return entry;
        }

        private static string RowId(JObject row)
        {
            string? id = row.Value<string>("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new JsonSerializationException(
                    "A value row storing packed children has no non-empty string "
                    + "\"id\". Its own id is half of every canonical id its children "
                    + "derive, so decoding cannot proceed without it.");
            }
            return id!;
        }

        private static string? RowMapKey(JObject row)
        {
            JToken? mapKey = row["mapKey"];
            return mapKey?.Type == JTokenType.String ? mapKey.Value<string>() : null;
        }

        /// <summary>
        /// Mirrors the web's <c>isCollapseStampedInstanceRoot</c>: the literal
        /// half of a value container that carries an arm of the P75 creation
        /// stamp. This is what makes a packed child's construction root the
        /// nearest enclosing stamped row rather than the top-level row, and it
        /// must answer exactly as
        /// <see cref="NeoClient.IsVirtualInstanceRoot"/> does on the typed row.
        /// </summary>
        private static bool IsCollapseStampedInstanceRoot(JObject content)
        {
            if (!IsLiteralValueContent(content)) return false;
            if (content.Property("instanceConstructorId") is not null) return true;
            return content["instanceVariantId"]?.Type == JTokenType.String;
        }

        /// <summary>Mirrors the web's <c>isLiteralValueContent</c>.</summary>
        private static bool IsLiteralValueContent(JObject content)
        {
            if (content.Property("value") is null) return false;
            JToken? init = content["init"];
            if (init is not null && init.Type != JTokenType.Null) return false;
            JToken? classId = content["classId"];
            if (classId is not null
                && classId.Type != JTokenType.Null
                && classId.Type != JTokenType.String)
            {
                return false;
            }
            foreach (string field in InstanceProvenanceFields)
            {
                JToken? provenance = content[field];
                if (provenance is null || provenance.Type == JTokenType.Null) continue;
                if (provenance.Type != JTokenType.String) return false;
                if (provenance.Value<string>()!.Length == 0) return false;
            }
            JToken? constructorArgs = content["constructorArgs"];
            if (constructorArgs is null) return true;
            if (constructorArgs.Type == JTokenType.Null) return false;
            if (classId?.Type != JTokenType.String) return false;
            if (content["value"] is not JObject) return false;
            if (constructorArgs is not JObject args) return false;
            foreach (JProperty argument in args.Properties())
            {
                if (argument.Name.Length == 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Mirrors the web's <c>isInitValueContent</c>. The initializer's
        /// compiled IR is not validated here: the packed decoder runs before
        /// deserialization and the <c>InitializerBody</c> converter already
        /// rejects a malformed body by name.
        /// </summary>
        private static bool IsInitValueContent(JObject content)
        {
            if (content["init"] is not JObject init) return false;
            string? code = init.Value<string>("code");
            if (string.IsNullOrEmpty(code)) return false;
            if (content.Property("value") is not null) return false;
            if (content.Property("constructorArgs") is not null) return false;
            foreach (string field in InstanceProvenanceFields)
            {
                if (content.Property(field) is not null) return false;
            }
            JToken? classId = content["classId"];
            return classId is null || classId.Type == JTokenType.Null;
        }

        private static bool IsNumber(JToken? token) =>
            token is not null
            && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);

        private readonly struct Scope
        {
            internal Scope(string ownerLabel, string? constructionRootId)
            {
                OwnerLabel = ownerLabel;
                ConstructionRootId = constructionRootId;
            }

            /// <summary>Names the owning row in every error raised beneath it.</summary>
            internal string OwnerLabel { get; }

            /// <summary>
            /// The nearest enclosing row carrying a P75 construction stamp, or
            /// null when there is none. Half of every canonical child id.
            /// </summary>
            internal string? ConstructionRootId { get; }
        }

        private sealed class DecodeState
        {
            internal DecodeState(string rowId, string? mapKey)
            {
                SeenIds = new HashSet<string> { rowId };
                MapKey = mapKey;
            }

            /// <summary>
            /// Every id already claimed inside this physical row, seeded with
            /// the row's own. Two positions resolving to one id would silently
            /// drop a subtree.
            /// </summary>
            internal HashSet<string> SeenIds { get; }

            /// <summary>
            /// The owning row's storage partition. A packed child is serialized
            /// inside its parent, so it inherits it.
            /// </summary>
            internal string? MapKey { get; }
        }
    }

    /// <summary>
    /// P76 §5 — rejects a <c>~packed</c> envelope that reached a typed value
    /// converter, which can only mean it is in a position packing never
    /// produces or that its row set skipped
    /// <see cref="NeoPackedValue.Expand"/>.
    ///
    /// <para>Fail-closed matters more here than for most malformed data. A
    /// packed envelope sitting unexpanded in a Class or dictionary body is a
    /// child-id position holding an object, and the row's own payload type is
    /// <c>Dictionary&lt;string, string&gt;</c> — so without this guard the read
    /// fails deep inside Newtonsoft with a message about dictionary conversion,
    /// or (for an array body) succeeds and hands the runtime a child id of
    /// <c>"{}"</c>. Either way the whole subtree beneath the envelope
    /// disappears, silently, which is precisely the failure export schema 31
    /// exists to prevent.</para>
    /// </summary>
    internal static class PackedValuePositionGuard
    {
        internal static void RejectUnexpanded(JObject carrier, string subject)
        {
            if (NeoPackedValue.IsEnvelope(carrier["value"]))
            {
                throw new JsonSerializationException(
                    $"{subject} holds a '{NeoPackedValue.EnvelopeKey}' envelope as its "
                    + "whole value. A packed child occupies a position INSIDE its "
                    + "parent's content, never the content itself; re-export the "
                    + "project from the current web app.");
            }
            if (!NeoPackedValue.RowCarriesPackedContent(carrier)) return;
            throw new JsonSerializationException(
                $"{subject} carries an unexpanded '{NeoPackedValue.EnvelopeKey}' child "
                + "envelope. Packed rows are expanded once, where the row set is "
                + "assembled, so this row reached the value converter without passing "
                + "through that boundary.");
        }

        /// <summary>
        /// A member declaration default is never an owning parent row — it has
        /// no id for a child to derive from and no stored position to be
        /// packed into — so an envelope there is invalid wherever it came from.
        /// Same rule, and same reason, as
        /// <see cref="PartialLeafPositionGuard.RejectDefaultCarrier"/>.
        /// </summary>
        internal static void RejectDefaultCarrier(JObject? carrier, string subject)
        {
            if (carrier is null) return;
            if (!NeoPackedValue.IsEnvelope(carrier["value"])
                && !NeoPackedValue.RowCarriesPackedContent(carrier))
            {
                return;
            }
            throw new JsonSerializationException(
                $"{subject} holds a '{NeoPackedValue.EnvelopeKey}' packed-child "
                + "envelope. Packing stores a child inside its owning parent VALUE "
                + "row, and a declaration default owns no row; declare the child "
                + "value normally instead.");
        }
    }
}
