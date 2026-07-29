// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal sealed class NeoAnimationDefinition
    {
        private readonly IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites;
        private IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites;
        private readonly IReadOnlyDictionary<int, Action[]> actions;
        private readonly Action[] prepareActions;

        internal NeoAnimationDefinition(
            int fps,
            int duration,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites,
            IReadOnlyDictionary<int, Action[]> actions,
            Action[] prepareActions)
        {
            FPS = fps;
            Duration = duration;
            this.sparseWrites = sparseWrites;
            this.resolvedWrites = resolvedWrites;
            this.actions = actions;
            this.prepareActions = prepareActions;
        }

        internal int FPS { get; }
        internal int Duration { get; }

        internal void PreparePlayback()
        {
            // Root fallback is captured when playback starts, not when the
            // generated clip handle is first requested. A pass then reuses
            // that stable snapshot for wraps and reverse traversal.
            resolvedWrites = NeoAnimationCompiler.ResolveFrames(Duration, sparseWrites);
            foreach (Action prepare in prepareActions) prepare();
        }

        internal void ApplyFrame(int frameIndex, bool useResolvedState)
        {
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> source =
                useResolvedState ? resolvedWrites : sparseWrites;
            if (source.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[] writes))
            {
                foreach (NeoAnimationCompiledWrite write in writes) write.Apply();
            }
            if (actions.TryGetValue(frameIndex, out Action[] frameActions))
            {
                foreach (Action action in frameActions) action();
            }
        }

        /// <summary>
        /// The compiled writes for one frame as authored (no resolved-state
        /// chain). Exposed for tests that assert the P42 §1.2 same-frame merge
        /// produced a single write rather than one per field.
        /// </summary>
        internal NeoAnimationCompiledWrite[] SparseWritesForFrame(int frameIndex)
        {
            return sparseWrites.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[] writes)
                ? writes
                : Array.Empty<NeoAnimationCompiledWrite>();
        }

        /// <summary>
        /// The resolved-state writes for one frame — what backward, wrapping,
        /// and boomerang traversal enter the frame with.
        /// </summary>
        internal NeoAnimationCompiledWrite[] ResolvedWritesForFrame(int frameIndex)
        {
            return resolvedWrites.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[] writes)
                ? writes
                : Array.Empty<NeoAnimationCompiledWrite>();
        }
    }

    /// <summary>
    /// The structured-leaf member kinds P42 §1.1 makes field-addressable.
    /// <see cref="None"/> covers every other member kind — a <c>$partial</c>
    /// envelope landing on one of those is invalid data, not a field write.
    /// </summary>
    internal enum NeoAnimationLeafKind
    {
        None = 0,
        Sprite,
        Vector2,
        Vector2Int,
        Vector3,
        Vector3Int,
        Color,
    }

    /// <summary>
    /// One validated field of a P42 <c>$partial</c> envelope, already checked
    /// against its member kind at compile time so <c>Apply()</c> does no
    /// validation and allocates nothing on the common path.
    /// <para>Sprite <c>fileId</c> is the only text-valued field; every other
    /// field is a number (a vector/colour component, or an integral
    /// <c>sliceIndex</c>).</para>
    /// </summary>
    internal readonly struct NeoAnimationLeafFieldValue
    {
        private NeoAnimationLeafFieldValue(string key, string? text, double number, bool isText)
        {
            Key = key;
            Text = text;
            Number = number;
            IsText = isText;
        }

        internal string Key { get; }
        internal string? Text { get; }
        internal double Number { get; }
        internal bool IsText { get; }

        internal static NeoAnimationLeafFieldValue OfText(string key, string? text)
        {
            return new NeoAnimationLeafFieldValue(key, text, 0d, true);
        }

        internal static NeoAnimationLeafFieldValue OfNumber(string key, double number)
        {
            return new NeoAnimationLeafFieldValue(key, null, number, false);
        }
    }

    /// <summary>
    /// The P42 §1.1 field table, and the kind-aware validation the JSON row
    /// layer deliberately cannot do (it has no member kind): which field names
    /// are legal per kind, which components must be integral, and the colour
    /// channel <c>[0, 1]</c> rule — which <b>rejects</b>, never clamps
    /// (decision D2; §7.4 and its acceptance checkbox are stale, §1.4 is
    /// correct, and <c>NeoColorValueConverter</c> already rejects on
    /// deserialize).
    /// </summary>
    internal static class NeoAnimationLeafFields
    {
        internal const string FileIdKey = "fileId";
        internal const string SliceIndexKey = "sliceIndex";

        private static readonly string[] SpriteKeys = { FileIdKey, SliceIndexKey };
        private static readonly string[] Vector2Keys = { "x", "y" };
        private static readonly string[] Vector3Keys = { "x", "y", "z" };
        private static readonly string[] ColorKeys = { "r", "g", "b", "a" };
        private static readonly string[] NoKeys = Array.Empty<string>();

        internal static NeoAnimationLeafKind KindOf(Member member)
        {
            return member switch
            {
                SpriteMember => NeoAnimationLeafKind.Sprite,
                Vector2Member => NeoAnimationLeafKind.Vector2,
                Vector2IntMember => NeoAnimationLeafKind.Vector2Int,
                Vector3Member => NeoAnimationLeafKind.Vector3,
                Vector3IntMember => NeoAnimationLeafKind.Vector3Int,
                ColorMember => NeoAnimationLeafKind.Color,
                _ => NeoAnimationLeafKind.None,
            };
        }

        internal static string[] LegalKeys(NeoAnimationLeafKind kind)
        {
            return kind switch
            {
                NeoAnimationLeafKind.Sprite => SpriteKeys,
                NeoAnimationLeafKind.Vector2 => Vector2Keys,
                NeoAnimationLeafKind.Vector2Int => Vector2Keys,
                NeoAnimationLeafKind.Vector3 => Vector3Keys,
                NeoAnimationLeafKind.Vector3Int => Vector3Keys,
                NeoAnimationLeafKind.Color => ColorKeys,
                _ => NoKeys,
            };
        }

        internal static string Describe(NeoAnimationLeafKind kind)
        {
            return kind switch
            {
                NeoAnimationLeafKind.Sprite => "Sprite",
                NeoAnimationLeafKind.Vector2 => "Vector2",
                NeoAnimationLeafKind.Vector2Int => "Vector2Int",
                NeoAnimationLeafKind.Vector3 => "Vector3",
                NeoAnimationLeafKind.Vector3Int => "Vector3Int",
                NeoAnimationLeafKind.Color => "Color",
                _ => "non-structured",
            };
        }

        internal static bool IsLegalKey(NeoAnimationLeafKind kind, string key)
        {
            foreach (string legal in LegalKeys(kind))
            {
                if (string.Equals(legal, key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static bool RequiresIntegerComponents(NeoAnimationLeafKind kind)
        {
            return kind == NeoAnimationLeafKind.Sprite
                || kind == NeoAnimationLeafKind.Vector2Int
                || kind == NeoAnimationLeafKind.Vector3Int;
        }

        /// <summary>
        /// Validates every written field against <paramref name="kind"/> and
        /// lowers it to the typed form the compiled write applies.
        /// <paramref name="where"/> is the caller's already-built
        /// "clip / frame / path" prefix, so every diagnostic names the clip,
        /// the frame index, and the member.
        /// </summary>
        internal static List<NeoAnimationLeafFieldValue> Compile(
            NeoAnimationLeafKind kind,
            NeoPartialLeafValue partial,
            string where)
        {
            var compiled = new List<NeoAnimationLeafFieldValue>(partial.FieldCount);
            foreach (string key in partial.FieldKeys)
            {
                if (!IsLegalKey(kind, key))
                {
                    throw new InvalidOperationException(
                        $"{where} writes field '{key}', which is not a field of a {Describe(kind)} member. Legal fields: {string.Join(", ", LegalKeys(kind))}.");
                }
                if (kind == NeoAnimationLeafKind.Sprite
                    && string.Equals(key, FileIdKey, StringComparison.Ordinal))
                {
                    if (partial.IsNullField(key))
                    {
                        compiled.Add(NeoAnimationLeafFieldValue.OfText(key, null));
                        continue;
                    }
                    if (!partial.TryGetString(key, out string? fileId))
                    {
                        throw new InvalidOperationException(
                            $"{where} field 'fileId' must be a file id string or null.");
                    }
                    compiled.Add(NeoAnimationLeafFieldValue.OfText(key, fileId));
                    continue;
                }
                if (!partial.TryGetDouble(key, out double number))
                {
                    throw new InvalidOperationException(
                        $"{where} field '{key}' must be a finite number.");
                }
                if (RequiresIntegerComponents(kind) && number != Math.Truncate(number))
                {
                    throw new InvalidOperationException(
                        $"{where} field '{key}' must be an integer on a {Describe(kind)} member; found {number}.");
                }
                if (kind == NeoAnimationLeafKind.Color && (number < 0d || number > 1d))
                {
                    // P42 decision D2: rejected, never clamped.
                    throw new InvalidOperationException(
                        $"{where} colour channel '{key}' must be within [0, 1]; found {number}.");
                }
                compiled.Add(NeoAnimationLeafFieldValue.OfNumber(key, number));
            }
            return compiled;
        }

        /// <summary>
        /// P42 §1.2/§1.4: composes <paramref name="fields"/> onto the leaf's
        /// value <b>as it stands</b> in <paramref name="current"/> and returns
        /// the whole composed value. Returns null when there is nothing to
        /// merge into — §1.4's "null leaf at apply time" — with
        /// <paramref name="skipReason"/> saying so, because inventing a base
        /// value is the one thing a field write must never do.
        ///
        /// <para>A field the composed leaf does not carry is <b>ignored</b>,
        /// never applied. This mirrors the web resolver's
        /// <c>applyStructuredLeafPartial</c> ("a field write can only overwrite
        /// a key the leaf already carries") and it is the reason every case
        /// below matches its own keys explicitly instead of falling through to
        /// a last component: an unrecognised key smeared onto <c>y</c>,
        /// <c>z</c>, <c>a</c>, or <c>sliceIndex</c> would compose a value the
        /// author never wrote, which is worse in every way than composing
        /// nothing. Applying it is not an option either — every whole-leaf
        /// guard is exact-keyed, so the result would be a record no value
        /// guard accepts and the CLI refuses to emit back to source.</para>
        ///
        /// <para><see cref="Compile"/> already rejects an unrecognised key at
        /// export-validation time, which is where an author gets told. This is
        /// the second layer, for field lists that reach apply time anyway.</para>
        /// </summary>
        internal static object? Compose(
            NeoAnimationLeafKind kind,
            IReadOnlyList<NeoAnimationLeafFieldValue> fields,
            MemberValue? current,
            out string? skipReason)
        {
            skipReason = null;
            switch (kind)
            {
                case NeoAnimationLeafKind.Sprite:
                {
                    if (current is not SpriteMemberValue spriteRow || spriteRow.value is null)
                    {
                        skipReason = NullLeafSkipReason;
                        return null;
                    }
                    var composed = new SpriteValue
                    {
                        fileId = spriteRow.value.fileId,
                        sliceIndex = spriteRow.value.sliceIndex,
                    };
                    foreach (NeoAnimationLeafFieldValue field in fields)
                    {
                        if (Is(field, FileIdKey)) composed.fileId = field.Text!;
                        else if (Is(field, SliceIndexKey)) composed.sliceIndex = (int)field.Number;
                    }
                    return composed;
                }
                case NeoAnimationLeafKind.Vector2:
                case NeoAnimationLeafKind.Vector2Int:
                {
                    if (current is not Vector2MemberValue vector2Row || vector2Row.value is null)
                    {
                        skipReason = NullLeafSkipReason;
                        return null;
                    }
                    var composed = new NeoVector2Value
                    {
                        x = vector2Row.value.x,
                        y = vector2Row.value.y,
                    };
                    foreach (NeoAnimationLeafFieldValue field in fields)
                    {
                        if (Is(field, "x")) composed.x = (float)field.Number;
                        else if (Is(field, "y")) composed.y = (float)field.Number;
                    }
                    return composed;
                }
                case NeoAnimationLeafKind.Vector3:
                case NeoAnimationLeafKind.Vector3Int:
                {
                    if (current is not Vector3MemberValue vector3Row || vector3Row.value is null)
                    {
                        skipReason = NullLeafSkipReason;
                        return null;
                    }
                    var composed = new NeoVector3Value
                    {
                        x = vector3Row.value.x,
                        y = vector3Row.value.y,
                        z = vector3Row.value.z,
                    };
                    foreach (NeoAnimationLeafFieldValue field in fields)
                    {
                        if (Is(field, "x")) composed.x = (float)field.Number;
                        else if (Is(field, "y")) composed.y = (float)field.Number;
                        else if (Is(field, "z")) composed.z = (float)field.Number;
                    }
                    return composed;
                }
                case NeoAnimationLeafKind.Color:
                {
                    if (current is not ColorMemberValue colorRow || colorRow.value is null)
                    {
                        skipReason = NullLeafSkipReason;
                        return null;
                    }
                    var composed = new NeoColorValue
                    {
                        r = colorRow.value.r,
                        g = colorRow.value.g,
                        b = colorRow.value.b,
                        a = colorRow.value.a,
                    };
                    foreach (NeoAnimationLeafFieldValue field in fields)
                    {
                        if (Is(field, "r")) composed.r = (float)field.Number;
                        else if (Is(field, "g")) composed.g = (float)field.Number;
                        else if (Is(field, "b")) composed.b = (float)field.Number;
                        else if (Is(field, "a")) composed.a = (float)field.Number;
                    }
                    return composed;
                }
                default:
                    skipReason = "its member kind has no addressable fields";
                    return null;
            }
        }

        internal const string NullLeafSkipReason =
            "its current value is null, so there is no record to merge the field into";

        private static bool Is(NeoAnimationLeafFieldValue field, string key)
        {
            return string.Equals(field.Key, key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// One flattened animation write. Two shapes share this type:
    ///
    /// <list type="bullet">
    /// <item><b>Whole-leaf write</b> — a compile-time constant
    /// <see cref="NeoValueWritePayload"/>, exactly as before P42.</item>
    /// <item><b>Field write</b> (P42 §4.3) — a validated set of
    /// <see cref="NeoAnimationLeafFieldValue"/>s composed against the node's
    /// <b>current</b> leaf value every time the frame is entered. The composed
    /// value is deliberately never cached: caching it would freeze a
    /// runtime-rebound sprite file (or a vector component the game wrote
    /// between frames) for the rest of the pass.</item>
    /// </list>
    ///
    /// <para>Per-instance node resolution is unchanged in both shapes: the
    /// writable parent and key are resolved once, in the constructor. Only the
    /// value computation moved to apply time.</para>
    /// </summary>
    internal sealed class NeoAnimationCompiledWrite
    {
        private static readonly List<NeoAnimationLeafFieldValue> NoFields = new();

        private readonly NeoClient client;
        private readonly NeoMemberClass target;
        private readonly string[] path;
        private readonly NeoValueWritePayload? payload;
        private readonly NeoAnimationLeafKind fieldKind;
        private readonly List<NeoAnimationLeafFieldValue>? fields;
        private readonly string clipKey;
        private readonly int frameIndex;
        private readonly NeoMemberClassWritable writableParent;
        private readonly string writableKey;
        private string? pathKeyCache;
        private string? skipKeyCache;

        internal NeoAnimationCompiledWrite(
            NeoClient client,
            NeoMemberClass target,
            string[] path,
            NeoValueWritePayload payload,
            string clipKey,
            int frameIndex)
        {
            this.client = client;
            this.target = target;
            this.path = path;
            this.payload = payload;
            fieldKind = NeoAnimationLeafKind.None;
            fields = null;
            this.clipKey = clipKey;
            this.frameIndex = frameIndex;
            writableParent = ResolveWritableParent(target, path);
            writableKey = path[path.Length - 1];
        }

        internal NeoAnimationCompiledWrite(
            NeoClient client,
            NeoMemberClass target,
            string[] path,
            NeoAnimationLeafKind fieldKind,
            List<NeoAnimationLeafFieldValue> fields,
            string clipKey,
            int frameIndex)
        {
            this.client = client;
            this.target = target;
            this.path = path;
            payload = null;
            this.fieldKind = fieldKind;
            this.fields = fields;
            this.clipKey = clipKey;
            this.frameIndex = frameIndex;
            writableParent = ResolveWritableParent(target, path);
            writableKey = path[path.Length - 1];
        }

        /// <summary>
        /// Re-keys an already-resolved field write with a different field set.
        /// Reuses the template's writable parent so building the resolved-state
        /// chain (once per <c>PreparePlayback</c>, per frame, per leaf) does not
        /// re-walk the node graph.
        /// </summary>
        private NeoAnimationCompiledWrite(
            NeoAnimationCompiledWrite template,
            List<NeoAnimationLeafFieldValue> fields,
            string clipKey,
            int frameIndex)
        {
            client = template.client;
            target = template.target;
            path = template.path;
            payload = null;
            fieldKind = template.fieldKind;
            this.fields = fields;
            this.clipKey = clipKey;
            this.frameIndex = frameIndex;
            writableParent = template.writableParent;
            writableKey = template.writableKey;
        }

        /// <summary>True when this write addresses fields rather than the whole leaf.</summary>
        internal bool IsFieldWrite => fields is not null;

        internal IReadOnlyList<NeoAnimationLeafFieldValue> Fields => fields ?? NoFields;

        /// <summary>
        /// LEAF-scoped merge identity. Deliberately does <b>not</b> include the
        /// field selector: <see cref="NeoAnimationCompiler.ResolveFrames"/>
        /// needs one identity per leaf so a whole-leaf write in a later frame
        /// can supersede the field writes accumulated before it under reverse
        /// traversal.
        /// </summary>
        internal string PathKey => pathKeyCache ??=
            $"{target.overrideValueId ?? target.value?.id ?? target.member.id}\u001e{string.Join("\u001f", path)}";

        /// <summary>
        /// The leaf identity plus the write's shape. A field write and a
        /// whole-leaf write to the same path are two different writes with two
        /// different resolved-state slots; this is the key that says so.
        /// </summary>
        internal string ResolutionKey => IsFieldWrite ? PathKey + "\u001d$field" : PathKey;

        /// <summary>
        /// Dedupe key for the P42 §1.4 apply-time skip warning: one warning per
        /// (clip, frame, member). Built lazily — the common path never skips,
        /// so a clip looping at 8 FPS allocates nothing per tick.
        /// </summary>
        private string SkipKey => skipKeyCache ??=
            $"{clipKey}\u001f{frameIndex}\u001f{ResolutionKey}";

        /// <summary>
        /// Snapshots the leaf's current <b>whole</b> value as the frame-0
        /// fallback of the resolved-state chain. Used for leaves the clip ever
        /// writes whole; leaves the clip only field-addresses use
        /// <see cref="ResolveFieldRoot"/> instead, so the snapshot cannot
        /// re-assert (and thereby freeze) fields the clip never touches.
        /// </summary>
        internal NeoAnimationCompiledWrite ResolveRoot()
        {
            NeoMember leaf = ResolveLeafNode();
            return new NeoAnimationCompiledWrite(
                client,
                target,
                path,
                NeoAnimationCompiler.Payload(leaf.value),
                clipKey,
                frameIndex);
        }

        /// <summary>
        /// Snapshots only <paramref name="fieldKeys"/> — the union of the field
        /// keys this clip writes on this leaf — as the frame-0 fallback for a
        /// field-only leaf. Returns null when there is no current value to
        /// snapshot (nothing to restore; the apply-time skip covers it).
        /// </summary>
        internal NeoAnimationCompiledWrite? ResolveFieldRoot(IReadOnlyList<string> fieldKeys)
        {
            MemberValue? current = ReadCurrentLeafRow();
            var snapshot = new List<NeoAnimationLeafFieldValue>(fieldKeys.Count);
            foreach (string key in fieldKeys)
            {
                // Same rule the compose side applies: a key the leaf does not
                // carry is ignored, so there is nothing to snapshot or restore
                // for it either. Without this the fallback below would read —
                // and later re-assert — whichever component the reader's last
                // branch happens to name.
                if (!NeoAnimationLeafFields.IsLegalKey(fieldKind, key)) continue;
                if (!TryReadCurrentField(current, key, out NeoAnimationLeafFieldValue value))
                {
                    return null;
                }
                snapshot.Add(value);
            }
            return new NeoAnimationCompiledWrite(this, snapshot, clipKey, frameIndex);
        }

        /// <summary>
        /// Folds <paramref name="other"/>'s fields into this write, in place.
        /// P42 §1.2: two field writes to one leaf in one frame collapse to a
        /// single write. Only ever called while the frame's write list is still
        /// being built, so no shared instance is mutated.
        /// </summary>
        internal void MergeFieldsFrom(NeoAnimationCompiledWrite other)
        {
            foreach (NeoAnimationLeafFieldValue field in other.fields!)
            {
                int index = IndexOfField(fields!, field.Key);
                if (index >= 0) fields![index] = field;
                else fields!.Add(field);
            }
        }

        /// <summary>
        /// Returns a NEW field write carrying this write's fields overlaid with
        /// <paramref name="later"/>'s. Used to accumulate the resolved-state
        /// chain without mutating the sparse writes, which every playback pass
        /// reuses.
        /// </summary>
        internal NeoAnimationCompiledWrite MergedWith(NeoAnimationCompiledWrite later)
        {
            var merged = new List<NeoAnimationLeafFieldValue>(fields!);
            foreach (NeoAnimationLeafFieldValue field in later.fields!)
            {
                int index = IndexOfField(merged, field.Key);
                if (index >= 0) merged[index] = field;
                else merged.Add(field);
            }
            return new NeoAnimationCompiledWrite(
                this,
                merged,
                later.clipKey,
                later.frameIndex);
        }

        internal void Apply()
        {
            if (writableParent.value is null) return;
            if (fields is null)
            {
                NeoGeneratedTypesSupport.SetValue(writableParent, writableKey, payload);
                return;
            }
            object? composed = ComposeFieldValue(out string? skipReason);
            if (composed is null)
            {
                ReportSkip(skipReason);
                return;
            }
            NeoGeneratedTypesSupport.SetValue(
                writableParent,
                writableKey,
                NeoValueWritePayload.FromValue(composed));
        }

        /// <summary>
        /// Read-modify-write against the leaf's value <b>as it stands right
        /// now</b> (P42 §1.4). Returns null when the write must be skipped, with
        /// <paramref name="skipReason"/> describing why.
        /// <para>The merge itself lives on <see cref="NeoAnimationLeafFields"/>
        /// because it is a pure function of (kind, fields, current row) and the
        /// cross-runtime fixture asserts it directly. The slice-count check
        /// stays here: it needs this write's client and asset database.</para>
        /// </summary>
        private object? ComposeFieldValue(out string? skipReason)
        {
            object? composed = NeoAnimationLeafFields.Compose(
                fieldKind,
                fields!,
                ReadCurrentLeafRow(),
                out skipReason);
            if (composed is not SpriteValue sprite) return composed;
            if (SliceIndexIsWithinResolvedFile(sprite)) return sprite;
            skipReason =
                $"slice index {sprite.sliceIndex} is outside the slice count of file '{sprite.fileId}'";
            return null;
        }

        private bool TryReadCurrentField(
            MemberValue? current,
            string key,
            out NeoAnimationLeafFieldValue value)
        {
            value = default;
            switch (fieldKind)
            {
                case NeoAnimationLeafKind.Sprite:
                {
                    if (current is not SpriteMemberValue spriteRow || spriteRow.value is null)
                    {
                        return false;
                    }
                    value = string.Equals(
                            key,
                            NeoAnimationLeafFields.FileIdKey,
                            StringComparison.Ordinal)
                        ? NeoAnimationLeafFieldValue.OfText(key, spriteRow.value.fileId)
                        : NeoAnimationLeafFieldValue.OfNumber(key, spriteRow.value.sliceIndex);
                    return true;
                }
                case NeoAnimationLeafKind.Vector2:
                case NeoAnimationLeafKind.Vector2Int:
                {
                    if (current is not Vector2MemberValue vector2Row || vector2Row.value is null)
                    {
                        return false;
                    }
                    value = NeoAnimationLeafFieldValue.OfNumber(
                        key,
                        string.Equals(key, "x", StringComparison.Ordinal)
                            ? vector2Row.value.x
                            : vector2Row.value.y);
                    return true;
                }
                case NeoAnimationLeafKind.Vector3:
                case NeoAnimationLeafKind.Vector3Int:
                {
                    if (current is not Vector3MemberValue vector3Row || vector3Row.value is null)
                    {
                        return false;
                    }
                    double component;
                    if (string.Equals(key, "x", StringComparison.Ordinal))
                    {
                        component = vector3Row.value.x;
                    }
                    else if (string.Equals(key, "y", StringComparison.Ordinal))
                    {
                        component = vector3Row.value.y;
                    }
                    else
                    {
                        component = vector3Row.value.z;
                    }
                    value = NeoAnimationLeafFieldValue.OfNumber(key, component);
                    return true;
                }
                case NeoAnimationLeafKind.Color:
                {
                    if (current is not ColorMemberValue colorRow || colorRow.value is null)
                    {
                        return false;
                    }
                    double channel;
                    if (string.Equals(key, "r", StringComparison.Ordinal))
                    {
                        channel = colorRow.value.r;
                    }
                    else if (string.Equals(key, "g", StringComparison.Ordinal))
                    {
                        channel = colorRow.value.g;
                    }
                    else if (string.Equals(key, "b", StringComparison.Ordinal))
                    {
                        channel = colorRow.value.b;
                    }
                    else
                    {
                        channel = colorRow.value.a;
                    }
                    value = NeoAnimationLeafFieldValue.OfNumber(key, channel);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Live read of the leaf's row. Goes to the client's overlay rather than
        /// relying only on the cached node value, so a write made by game code
        /// between two ticks is always visible.
        /// </summary>
        private MemberValue? ReadCurrentLeafRow()
        {
            if (!writableParent.TryGet(writableKey, out NeoMember? leaf)) return null;
            if (writableParent.value?.value is not null
                && writableParent.value.value.TryGetValue(writableKey, out string leafValueId)
                && client.TryGetOverlaidValue(leaf.ownership, leafValueId, out MemberValue? row))
            {
                return row;
            }
            return leaf.value;
        }

        /// <summary>
        /// P42 §1.4: a slice index outside the resolved file's slice count is a
        /// runtime condition, not an authoring error. Only the synchronized
        /// asset database knows the count — when it is absent, or does not carry
        /// the file, or carries no slices for it, the count is unknown and the
        /// write proceeds. A negative index is knowably invalid either way.
        /// </summary>
        private bool SliceIndexIsWithinResolvedFile(SpriteValue composed)
        {
            if (composed.sliceIndex < 0) return false;
            NeoAssetDatabase? database = client.assetDatabase;
            if (database is null) return true;
            if (string.IsNullOrEmpty(composed.fileId)) return true;
            foreach (NeoAssetDatabaseEntry entry in database.Files)
            {
                if (!string.Equals(entry.FileId, composed.fileId, StringComparison.Ordinal))
                {
                    continue;
                }
                int sliceCount = entry.Sprites.Length;
                if (sliceCount == 0) return true;
                return composed.sliceIndex < sliceCount;
            }
            return true;
        }

        private void ReportSkip(string? reason)
        {
            if (!client.ShouldReportAnimationApplySkip(SkipKey)) return;
            UnityEngine.Debug.LogWarning(
                $"Animation clip '{clipKey}' frame {frameIndex} skipped the field write to '{string.Join(".", path)}': {reason ?? "the value could not be composed"}. The rest of the clip still plays.");
        }

        private static int IndexOfField(
            List<NeoAnimationLeafFieldValue> fields,
            string key)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                if (string.Equals(fields[index].Key, key, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private NeoMember ResolveLeafNode()
        {
            NeoMemberClass parent = target;
            for (int index = 0; index < path.Length - 1; index++)
            {
                if (!parent.TryGet(path[index], out NeoMemberClass? child)
                    || child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
                }
                parent = child;
            }
            if (!parent.TryGet(path[path.Length - 1], out NeoMember? leaf))
            {
                throw new InvalidOperationException(
                    $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
            }
            return leaf;
        }

        private static NeoMemberClassWritable ResolveWritableParent(
            NeoMemberClass target,
            string[] path)
        {
            NeoMemberClassWritable parent = target.AsWritableView();
            for (int index = 0; index < path.Length - 1; index++)
            {
                if (!parent.TryGet(path[index], out NeoMemberClass? child)
                    || child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
                }
                parent = child.AsWritableView();
            }
            return parent;
        }
    }

    internal static class NeoAnimationCompiler
    {
        private const string AnimationClipWorldKind = "animationClip";

        internal static void ValidateProject(NeoClient client)
        {
            var validated = new HashSet<string>(StringComparer.Ordinal);
            foreach (NeoSchemaClass owner in client.classes.Values)
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> env =
                    NeoGenericResolution.ResolveEnv(client, owner.id);
                foreach (MergedSchemaEntry placement in
                    client.ResolveInstanceSurfaceSchema(owner.id))
                {
                    if (!client.TryGetMember(placement.memberId, out Member? rawMember))
                    {
                        continue;
                    }
                    if (rawMember is GenericMember genericMember
                        && env.TryGetValue(genericMember.genericParamId, out NeoGenericEnvEntry entry)
                        && !entry.IsBound)
                    {
                        // Open generic class declarations are not instantiable.
                        // Their inherited placement is validated through each
                        // closed subclass, after TTarget substitution.
                        continue;
                    }
                    Member resolvedMember = NeoGenericResolution.SubstituteMember(
                        client,
                        rawMember,
                        env);
                    if (resolvedMember is not ClassMember clipMember) continue;
                    if (!string.Equals(
                            ResolveWorldKind(client, clipMember.classId),
                            AnimationClipWorldKind,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    ValidateExportClip(
                        client,
                        owner,
                        placement.schemaKey,
                        clipMember,
                        validated,
                        new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        private static (int fps, int duration) ValidateExportClip(
            NeoClient client,
            NeoSchemaClass targetClass,
            string clipKey,
            ClassMember clipMember,
            HashSet<string> validated,
            HashSet<string> stack)
        {
            string validationKey = $"{targetClass.id}\u001f{clipMember.id}";
            if (!stack.Add(validationKey))
            {
                throw new InvalidOperationException(
                    $"Animation child-track cycle reaches clip '{clipKey}' on class '{targetClass.name}'.");
            }
            try
            {
                var clipNode = new NeoMemberClass(client, clipMember, null);
                int fps = ReadRequiredInt(clipNode, "FPS", clipKey);
                int duration = ReadRequiredInt(clipNode, "Duration", clipKey);
                if (fps < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' FPS must be at least 1; found {fps}.");
                }
                if (duration < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' Duration must be at least 1; found {duration}.");
                }
                if (!validated.Add(validationKey)) return (fps, duration);

                var frameIndexes = new HashSet<int>();
                if (clipNode.TryGet("Frames", out NeoMemberList? frames))
                {
                    foreach (NeoMember item in frames)
                    {
                        if (item is not NeoMemberClass frame)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class frame row.");
                        }
                        int frameIndex = ReadRequiredInt(frame, "Index", clipKey);
                        if (frameIndex < 0 || frameIndex >= duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' frame index {frameIndex} is outside [0, {duration - 1}].");
                        }
                        if (!frameIndexes.Add(frameIndex))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' has duplicate frame index {frameIndex}.");
                        }
                        if (frame.TryGet("Overrides", out NeoMemberClass? overrides)
                            && overrides.value is not null)
                        {
                            // No instance in hand at whole-clip validation, so
                            // definition presence resolves from declarations
                            // only; see ResolveDefinitionPresence.
                            ValidateExportOverrides(
                                client,
                                overrides,
                                targetRow: null,
                                Array.Empty<string>(),
                                clipKey,
                                frameIndex);
                        }
                        ValidateExportActions(
                            client,
                            targetClass.id,
                            frame,
                            clipKey,
                            frameIndex);
                        ValidateExportChildOverrides(client, frame, clipKey, frameIndex);
                    }
                }

                if (clipNode.TryGet("Tracks", out NeoMemberList? tracks))
                {
                    foreach (NeoMember item in tracks)
                    {
                        if (item is not NeoMemberClass track)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class child track row.");
                        }
                        string childId = ReadRequiredLookupId(
                            track,
                            "Child",
                            clipKey,
                            frameIndex: null);
                        string childClipKey = ReadRequiredString(track, "ClipKey", clipKey);
                        int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
                        if (startFrame < 0)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' StartFrame must be non-negative; found {startFrame}.");
                        }
                        if (client.ResolveEffectiveRow(childId) is not ObjectMemberValue child
                            || string.IsNullOrWhiteSpace(child.classId)
                            || !client.TryGetClass(child.classId!, out NeoSchemaClass? childClass))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' references missing child '{childId}'.");
                        }
                        ClassMember? childClipMember = null;
                        IReadOnlyDictionary<string, NeoGenericEnvEntry> childEnv =
                            NeoGenericResolution.ResolveEnv(client, childClass.id);
                        foreach (MergedSchemaEntry entry in
                            client.ResolveInstanceSurfaceSchema(childClass.id))
                        {
                            if (!string.Equals(entry.schemaKey, childClipKey, StringComparison.Ordinal))
                            {
                                continue;
                            }
                            if (client.TryGetMember(entry.memberId, out Member? childRawMember))
                            {
                                childClipMember = NeoGenericResolution.SubstituteMember(
                                    client,
                                    childRawMember,
                                    childEnv) as ClassMember;
                            }
                            break;
                        }
                        if (childClipMember is null
                            || !string.Equals(
                                ResolveWorldKind(client, childClipMember.classId),
                                AnimationClipWorldKind,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' does not resolve to an animation clip on child class '{childClass.name}'.");
                        }
                        (int childFps, int childDuration) = ValidateExportClip(
                            client,
                            childClass,
                            childClipKey,
                            childClipMember,
                            validated,
                            stack);
                        int parentFrameLength = checked((int)Math.Ceiling(
                            childDuration * (double)fps / childFps));
                        if (startFrame + parentFrameLength > duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' ends at parent frame {startFrame + parentFrameLength}, past Duration {duration}.");
                        }
                    }
                }
                return (fps, duration);
            }
            finally
            {
                stack.Remove(validationKey);
            }
        }

        /// <summary>
        /// Load-time validation of one frame's override record (P29 §3.4, and
        /// P42 §4.4's structured-leaf additions).
        ///
        /// <para><paramref name="targetRow"/> is the row of the value the
        /// overrides are written against, when one is known. It exists so the
        /// "cannot descend into a null definition member" rule inspects the
        /// <b>target's</b> definition value — as the TS validator's
        /// <c>resolveDefinitionChild</c> does — rather than the override node's
        /// own child row, which is a different question entirely. Whole-clip
        /// validation has no instance to point at and passes null; child-override
        /// validation passes the placed child's row.</para>
        /// </summary>
        private static void ValidateExportOverrides(
            NeoClient client,
            NeoMemberClass partial,
            ObjectMemberValue? targetRow,
            string[] prefix,
            string clipKey,
            int frameIndex)
        {
            if (partial.value?.value is null) return;
            foreach (var pair in partial.value.value)
            {
                if (!partial.TryGet(pair.Key, out NeoMember? child))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains unknown override key '{pair.Key}'.");
                }
                string[] path = Append(prefix, pair.Key);
                string where = DescribeOverridePath(clipKey, frameIndex, path);
                NeoAnimationDefinitionPresence presence = ResolveDefinitionPresence(
                    client,
                    targetRow,
                    pair.Key,
                    child.member,
                    out ObjectMemberValue? definitionRecord);
                if (child is NeoMemberClass childClass)
                {
                    if (childClass.value is null)
                    {
                        throw new InvalidOperationException(
                            $"Animation clip '{clipKey}' frame {frameIndex} cannot descend through null Class path '{string.Join(".", path)}'.");
                    }
                    EnsureDefinitionIsDescendable(presence, where);
                    ValidateExportOverrides(
                        client,
                        childClass,
                        definitionRecord,
                        path,
                        clipKey,
                        frameIndex);
                    continue;
                }
                NeoAnimationLeafKind leafKind = NeoAnimationLeafFields.KindOf(child.member);
                NeoPartialLeafValue? partialFields = child.partialLeafValue?.value;
                if (partialFields is not null)
                {
                    if (leafKind == NeoAnimationLeafKind.None)
                    {
                        throw new InvalidOperationException(
                            $"{where} carries a '$partial' field envelope, but that member has no addressable fields. Only Sprite, Vector2(Int), Vector3(Int), and Color members do.");
                    }
                    // Eligibility is evaluated on the ENCLOSING leaf, never on
                    // the field — a field segment has no member id to walk.
                    EnsureEligibleExportLeaf(client, child, where);
                    NeoAnimationLeafFields.Compile(leafKind, partialFields, where);
                    EnsureDefinitionIsDescendable(presence, where);
                    continue;
                }
                EnsureNoDeeperFieldPath(client, leafKind, pair.Value, where);
                EnsureEligibleExportLeaf(client, child, where);
            }
        }

        private static void EnsureEligibleExportLeaf(
            NeoClient client,
            NeoMember child,
            string where)
        {
            if (child is NeoMemberList or NeoMemberDictionary
                || child.member is FunctionMember or NSFunctionMember or NSPropertyMember
                || child.member.isReadOnly == true
                || child.member.isStatic)
            {
                throw new InvalidOperationException(
                    $"{where} is not an eligible runtime-writable leaf.");
            }
            NeoValueOwnership? ownership = client.DeclaredOwnership(child.member);
            if (ownership != NeoValueOwnership.Save
                && ownership != NeoValueOwnership.Session)
            {
                throw new InvalidOperationException(
                    $"{where} resolves Immutable storage.");
            }
        }

        /// <summary>
        /// P42 §4.4: a field path may not be deeper than one level. The wire
        /// envelope is flat by construction (the row layer rejects nested
        /// objects), so the only way to express a deeper path is a Class-shaped
        /// override record parked on a structured-leaf member — which is what
        /// this rejects, by name.
        /// </summary>
        private static void EnsureNoDeeperFieldPath(
            NeoClient client,
            NeoAnimationLeafKind leafKind,
            string overrideValueId,
            string where)
        {
            if (leafKind == NeoAnimationLeafKind.None) return;
            if (client.ResolveEffectiveRow(overrideValueId) is not ObjectMemberValue) return;
            throw new InvalidOperationException(
                $"{where} addresses a field path deeper than one level. Structured leaves are one level deep: a '$partial' envelope names fields, never sub-fields.");
        }

        /// <summary>
        /// Whether the target's definition value at a path segment exists and is
        /// non-null. <see cref="NeoAnimationDefinitionPresence.Unknown"/> is
        /// deliberately <b>not</b> an error: .NET whole-clip validation runs
        /// against a class declaration with no instance in hand, and most Class
        /// members legitimately carry no class-level default.
        /// </summary>
        private static void EnsureDefinitionIsDescendable(
            NeoAnimationDefinitionPresence presence,
            string where)
        {
            if (presence != NeoAnimationDefinitionPresence.NullValue) return;
            throw new InvalidOperationException(
                $"{where} cannot descend into a null definition member — there is no record to merge into.");
        }

        private enum NeoAnimationDefinitionPresence
        {
            Unknown = 0,
            NullValue,
            Present,
        }

        /// <summary>
        /// Mirrors the TS validator's <c>resolveDefinitionChild</c> precedence:
        /// an explicit instance mapping wins, then the declaration's
        /// <c>defaultValue</c>, then the legacy <c>member.valueId</c> row.
        /// </summary>
        private static NeoAnimationDefinitionPresence ResolveDefinitionPresence(
            NeoClient client,
            ObjectMemberValue? targetRow,
            string schemaKey,
            Member declaration,
            out ObjectMemberValue? record)
        {
            record = null;
            if (targetRow?.value is not null
                && targetRow.value.TryGetValue(schemaKey, out string mappedValueId))
            {
                MemberValue? mapped = client.ResolveEffectiveRow(mappedValueId);
                if (mapped is null || mapped.IsRemoved)
                {
                    return NeoAnimationDefinitionPresence.NullValue;
                }
                record = mapped as ObjectMemberValue;
                return RowPresence(mapped);
            }
            NeoAnimationDefinitionPresence declared = DeclarationDefaultPresence(
                declaration,
                out record);
            if (declared != NeoAnimationDefinitionPresence.Unknown) return declared;
            if (!string.IsNullOrWhiteSpace(declaration.valueId))
            {
                MemberValue? legacy = client.ResolveEffectiveRow(declaration.valueId!);
                if (legacy is null || legacy.IsRemoved)
                {
                    return NeoAnimationDefinitionPresence.NullValue;
                }
                record = legacy as ObjectMemberValue;
                return RowPresence(legacy);
            }
            return NeoAnimationDefinitionPresence.Unknown;
        }

        private static NeoAnimationDefinitionPresence RowPresence(MemberValue row)
        {
            return row switch
            {
                ObjectMemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                SpriteMemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                FileMemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                Vector2MemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                Vector3MemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                ColorMemberValue typed => typed.value is null
                    ? NeoAnimationDefinitionPresence.NullValue
                    : NeoAnimationDefinitionPresence.Present,
                NullMemberValue => NeoAnimationDefinitionPresence.NullValue,
                _ => NeoAnimationDefinitionPresence.Present,
            };
        }

        private static NeoAnimationDefinitionPresence DeclarationDefaultPresence(
            Member declaration,
            out ObjectMemberValue? record)
        {
            record = null;
            switch (declaration)
            {
                case ClassMember typed:
                    if (typed.defaultValue is null) return NeoAnimationDefinitionPresence.Unknown;
                    if (typed.defaultValue.value is null)
                    {
                        return NeoAnimationDefinitionPresence.NullValue;
                    }
                    record = new ObjectMemberValue
                    {
                        value = typed.defaultValue.value,
                        classId = typed.defaultValue.classId,
                    };
                    return NeoAnimationDefinitionPresence.Present;
                case SpriteMember typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case AudioMember typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case Vector2Member typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case Vector2IntMember typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case Vector3Member typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case Vector3IntMember typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                case ColorMember typed:
                    return DefaultPresence(typed.defaultValue?.value is not null, typed.defaultValue is not null);
                default:
                    return NeoAnimationDefinitionPresence.Unknown;
            }
        }

        private static NeoAnimationDefinitionPresence DefaultPresence(
            bool hasValue,
            bool hasCarrier)
        {
            if (hasValue) return NeoAnimationDefinitionPresence.Present;
            return hasCarrier
                ? NeoAnimationDefinitionPresence.NullValue
                : NeoAnimationDefinitionPresence.Unknown;
        }

        private static string DescribeOverridePath(
            string clipKey,
            int frameIndex,
            string[] path)
        {
            return $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}'";
        }

        private static void ValidateExportActions(
            NeoClient client,
            string targetClassId,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("Actions", out NeoMemberList? actions)) return;
            foreach (NeoMember actionNode in actions)
            {
                if (actionNode is not NeoMemberFunctionRef functionRef
                    || string.IsNullOrWhiteSpace(functionRef.FunctionMemberId))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains an invalid FunctionRef action.");
                }
                string memberId = functionRef.FunctionMemberId!;
                EnsureTargetActionMember(
                    client,
                    targetClassId,
                    memberId,
                    clipKey,
                    frameIndex);
                if (client.TryResolveFunctionMember(memberId, out FunctionMember? native))
                {
                    ValidateActionSignature(
                        native.returnTypeInfo,
                        native.argumentTypes,
                        native.deferred == true,
                        memberId,
                        clipKey,
                        frameIndex);
                    continue;
                }
                NeoResolvedNSFunction? script = NeoNSFunctionRuntime.TryResolve(client, memberId);
                if (script is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' does not resolve to a Function or NSFunction.");
                }
                ValidateActionSignature(
                    script.ReturnTypeInfo,
                    script.ArgumentTypes,
                    script.Deferred,
                    memberId,
                    clipKey,
                    frameIndex);
            }
        }

        private static void ValidateExportChildOverrides(
            NeoClient client,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("ChildOverrides", out NeoMemberList? childOverrides)) return;
            foreach (NeoMember item in childOverrides)
            {
                if (item is not NeoMemberClass childOverride)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains a non-Class child override row.");
                }
                string childId = ReadRequiredLookupId(
                    childOverride,
                    "Child",
                    clipKey,
                    frameIndex);
                if (client.ResolveEffectiveRow(childId) is not ObjectMemberValue child)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} references missing child '{childId}'.");
                }
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(child.classId)
                    && !string.Equals(overrides.value.classId, child.classId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} child '{childId}' override class '{overrides.value.classId}' does not match '{child.classId}'.");
                }
                ValidateExportOverrides(
                    client,
                    overrides,
                    child,
                    Array.Empty<string>(),
                    clipKey,
                    frameIndex);
            }
        }

        internal static NeoAnimationDefinition Compile<T>(
            T target,
            string schemaKey)
            where T : NeoGeneratedClassValue
        {
            return Compile(target, schemaKey, new HashSet<string>(StringComparer.Ordinal));
        }

        private static NeoAnimationDefinition Compile(
            NeoGeneratedClassValue target,
            string schemaKey,
            HashSet<string> compileStack)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(schemaKey))
            {
                throw new ArgumentException("Clip schema key cannot be empty.", nameof(schemaKey));
            }
            string compileKey = $"{target.AnimationInstanceIdentity}\u001f{schemaKey}";
            if (!compileStack.Add(compileKey))
            {
                throw new InvalidOperationException(
                    $"Animation child-track cycle reaches clip '{schemaKey}' on value '{target.valueId ?? target.classId}'.");
            }
            try
            {
                if (!target.BackingNode.TryGet(schemaKey, out NeoMemberClass? clipNode))
                {
                    throw new InvalidOperationException(
                        $"Generated animation clip member '{schemaKey}' was not found on target class '{target.classId}'. Regenerate the project's C# types.");
                }
                string? worldKind = ResolveWorldKind(target.Client, clipNode.member.classId);
                if (!string.Equals(worldKind, AnimationClipWorldKind, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Member '{schemaKey}' targets class '{clipNode.member.classId}', whose inherited world kind is '{worldKind ?? "<missing>"}' instead of '{AnimationClipWorldKind}'.");
                }

                int fps = ReadRequiredInt(clipNode, "FPS", schemaKey);
                int duration = ReadRequiredInt(clipNode, "Duration", schemaKey);
                if (fps < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{schemaKey}' FPS must be at least 1; found {fps}.");
                }
                if (duration < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{schemaKey}' Duration must be at least 1; found {duration}.");
                }

                var sparseByIndex = new Dictionary<int, List<NeoAnimationCompiledWrite>>();
                var actionsByIndex = new Dictionary<int, Action[]>();
                var prepareActions = new List<Action>();
                var seenFrames = new HashSet<int>();
                if (clipNode.TryGet("Frames", out NeoMemberList? frames))
                {
                    foreach (NeoMember frameMember in frames)
                    {
                        if (frameMember is not NeoMemberClass frame)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' contains a non-Class frame row.");
                        }
                        int frameIndex = ReadRequiredInt(frame, "Index", schemaKey);
                        if (frameIndex < 0 || frameIndex >= duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' frame index {frameIndex} is outside [0, {duration - 1}].");
                        }
                        if (!seenFrames.Add(frameIndex))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' has duplicate frame index {frameIndex}.");
                        }

                        var writes = new List<NeoAnimationCompiledWrite>();
                        if (frame.TryGet("Overrides", out NeoMemberClass? overrides)
                            && overrides.value is not null)
                        {
                            FlattenOverrides(
                                target.Client,
                                target.BackingNode,
                                overrides,
                                Array.Empty<string>(),
                                target.ValueOwnership,
                                writes,
                                schemaKey,
                                frameIndex);
                        }
                        sparseByIndex[frameIndex] = writes;
                        actionsByIndex[frameIndex] = CompileActions(
                            target,
                            frame,
                            schemaKey,
                            frameIndex);

                        if (frame.TryGet("ChildOverrides", out NeoMemberList? childOverrides))
                        {
                            CompileChildOverrides(
                                target,
                                childOverrides,
                                writes,
                                schemaKey,
                                frameIndex);
                        }
                    }
                }
                if (clipNode.TryGet("Tracks", out NeoMemberList? tracks))
                {
                    CompileChildTracks(
                        target,
                        tracks,
                        fps,
                        duration,
                        actionsByIndex,
                        prepareActions,
                        schemaKey,
                        compileStack);
                }

                var sparse = new Dictionary<int, NeoAnimationCompiledWrite[]>();
                foreach (var pair in sparseByIndex) sparse[pair.Key] = pair.Value.ToArray();
                Dictionary<int, NeoAnimationCompiledWrite[]> resolved = ResolveFrames(
                    duration,
                    sparse);
                return new NeoAnimationDefinition(
                    fps,
                    duration,
                    sparse,
                    resolved,
                    actionsByIndex,
                    prepareActions.ToArray());
            }
            finally
            {
                compileStack.Remove(compileKey);
            }
        }

        private static void FlattenOverrides(
            NeoClient client,
            NeoMemberClass target,
            NeoMemberClass partial,
            string[] prefix,
            NeoValueOwnership inheritedOwnership,
            List<NeoAnimationCompiledWrite> writes,
            string clipKey,
            int frameIndex)
        {
            if (partial.value?.value is null) return;
            foreach (var pair in partial.value.value)
            {
                if (!partial.TryGet(pair.Key, out NeoMember? child))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains unknown override key '{pair.Key}'.");
                }
                string[] path = Append(prefix, pair.Key);
                string where = DescribeOverridePath(clipKey, frameIndex, path);
                NeoValueOwnership ownership =
                    client.DeclaredOwnership(child.member) ?? inheritedOwnership;
                if (child is NeoMemberClass childClass)
                {
                    if (childClass.value is null)
                    {
                        throw new InvalidOperationException(
                            $"Animation clip '{clipKey}' frame {frameIndex} cannot descend through null Class path '{string.Join(".", path)}'.");
                    }
                    // The override record being a record is not the question the
                    // rule is about — the TARGET must have a value to descend
                    // into. That is what the TS validator checks, and what the
                    // two .NET copies of this rule used to get wrong.
                    EnsureTargetIsDescendable(target, path, where);
                    FlattenOverrides(
                        client,
                        target,
                        childClass,
                        path,
                        ownership,
                        writes,
                        clipKey,
                        frameIndex);
                    continue;
                }

                // P42 §1.3: a structured leaf is a PATH SEGMENT when its
                // override row is a `$partial` envelope, and a write target when
                // it is a full value. The envelope decides which.
                NeoAnimationLeafKind leafKind = NeoAnimationLeafFields.KindOf(child.member);
                NeoPartialLeafValue? partialFields = child.partialLeafValue?.value;
                if (partialFields is not null)
                {
                    if (leafKind == NeoAnimationLeafKind.None)
                    {
                        throw new InvalidOperationException(
                            $"{where} carries a '$partial' field envelope, but that member has no addressable fields. Only Sprite, Vector2(Int), Vector3(Int), and Color members do.");
                    }
                    // Eligibility is evaluated on the ENCLOSING leaf member, not
                    // on the field: fields carry no storage of their own and a
                    // field segment has no member id to resolve.
                    EnsureEligibleCompiledLeaf(child, ownership, where);
                    List<NeoAnimationLeafFieldValue> compiledFields =
                        NeoAnimationLeafFields.Compile(leafKind, partialFields, where);
                    // `{"$partial":{}}` is the wire form of "no change", so it
                    // produces NO write at all — it must never become the frame
                    // the leaf's value is attributed to. Emitting an empty
                    // write would compose an identical value and then hand this
                    // frame the leaf's resolved-state slot (and, on a leaf the
                    // clip only field-addresses, a root snapshot), so a frame
                    // that authored nothing would read as the one that did.
                    // The web resolver skips it at collection for exactly this
                    // reason (`resolveSparseValueAtPath`).
                    if (compiledFields.Count == 0) continue;
                    EnsurePlacementPathIsIsolated(
                        client,
                        target,
                        path,
                        clipKey,
                        frameIndex);
                    AddFieldWrite(
                        client,
                        writes,
                        target,
                        path,
                        leafKind,
                        compiledFields,
                        clipKey,
                        frameIndex);
                    continue;
                }
                EnsureNoDeeperFieldPath(client, leafKind, pair.Value, where);
                EnsureEligibleCompiledLeaf(child, ownership, where);
                if (child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' has no override payload row.");
                }
                EnsurePlacementPathIsIsolated(
                    client,
                    target,
                    path,
                    clipKey,
                    frameIndex);
                writes.Add(new NeoAnimationCompiledWrite(
                    client,
                    target,
                    path,
                    Payload(child.value),
                    clipKey,
                    frameIndex));
            }
        }

        private static void EnsureEligibleCompiledLeaf(
            NeoMember child,
            NeoValueOwnership ownership,
            string where)
        {
            if (child is NeoMemberList or NeoMemberDictionary
                || child.member is FunctionMember or NSFunctionMember
                || child.member is NSPropertyMember
                || child.member.isReadOnly == true
                || child.member.isStatic)
            {
                throw new InvalidOperationException(
                    $"{where} is not an eligible runtime-writable leaf.");
            }
            if (ownership != NeoValueOwnership.Save
                && ownership != NeoValueOwnership.Session)
            {
                throw new InvalidOperationException(
                    $"{where} resolves Immutable storage.");
            }
        }

        /// <summary>
        /// P42 §1.2: two field writes to one leaf in one frame collapse to a
        /// SINGLE write. Every other flattening path appends unconditionally;
        /// this one folds into the leaf's existing field write when there is one.
        /// </summary>
        private static void AddFieldWrite(
            NeoClient client,
            List<NeoAnimationCompiledWrite> writes,
            NeoMemberClass target,
            string[] path,
            NeoAnimationLeafKind leafKind,
            List<NeoAnimationLeafFieldValue> fields,
            string clipKey,
            int frameIndex)
        {
            var write = new NeoAnimationCompiledWrite(
                client,
                target,
                path,
                leafKind,
                fields,
                clipKey,
                frameIndex);
            foreach (NeoAnimationCompiledWrite existing in writes)
            {
                if (!existing.IsFieldWrite) continue;
                if (!string.Equals(existing.PathKey, write.PathKey, StringComparison.Ordinal))
                {
                    continue;
                }
                existing.MergeFieldsFrom(write);
                return;
            }
            writes.Add(write);
        }

        /// <summary>
        /// The compile-time half of the "cannot descend into a null definition
        /// member" rule, evaluated against the played instance's own graph.
        /// A structured-leaf FIELD descent deliberately does not go through here:
        /// a leaf that is null on this instance is the P42 §1.4 apply-time skip,
        /// not a compile error.
        /// </summary>
        private static void EnsureTargetIsDescendable(
            NeoMemberClass target,
            string[] path,
            string where)
        {
            NeoMemberClass cursor = target;
            for (int index = 0; index < path.Length; index++)
            {
                if (!cursor.TryGet(path[index], out NeoMemberClass? child)
                    || child.value is null)
                {
                    throw new InvalidOperationException(
                        $"{where} cannot descend into a null definition member on target '{target.value?.classId ?? target.member.classId}'.");
                }
                cursor = child;
            }
        }

        private static void EnsurePlacementPathIsIsolated(
            NeoClient client,
            NeoMemberClass target,
            string[] path,
            string clipKey,
            int frameIndex)
        {
            if (path.Length == 0
                || target.value?.value is null
                || !target.value.value.TryGetValue("assetValueId", out string assetValueId)
                || client.ResolveEffectiveRow(assetValueId) is not ObjectMemberValue asset
                || asset.value is null
                || !target.value.value.TryGetValue(path[0], out string placedChildId)
                || !asset.value.TryGetValue(path[0], out string authoredChildId)
                || !string.Equals(placedChildId, authoredChildId, StringComparison.Ordinal))
            {
                return;
            }
            throw new InvalidOperationException(
                $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' still references shared authored row '{authoredChildId}' on placement '{target.value.id}'. Re-export with a placement-owned clone carrying sourceValueId before playback.");
        }

        private static void CompileChildOverrides(
            NeoGeneratedClassValue target,
            NeoMemberList childOverrides,
            List<NeoAnimationCompiledWrite> writes,
            string clipKey,
            int frameIndex)
        {
            foreach (NeoMember item in childOverrides)
            {
                if (item is not NeoMemberClass childOverride)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains a non-Class child override row.");
                }
                string sourceChildId = ReadRequiredLookupId(
                    childOverride,
                    "Child",
                    clipKey,
                    frameIndex);
                NeoMemberClass? placedChild = ResolvePlacedChild(
                    target.Client,
                    target.BackingNode,
                    sourceChildId,
                    clipKey,
                    $"frame {frameIndex} child override");
                if (placedChild is null)
                {
                    // Absent slot. Skipping is scoped to this one reference:
                    // the frame's other child overrides, its own Overrides, and
                    // its actions all still apply.
                    continue;
                }
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                FlattenOverrides(
                    target.Client,
                    placedChild,
                    overrides,
                    Array.Empty<string>(),
                    placedChild.ownership,
                    writes,
                    clipKey,
                    frameIndex);
            }
        }

        private static void CompileChildTracks(
            NeoGeneratedClassValue target,
            NeoMemberList tracks,
            int parentFps,
            int parentDuration,
            Dictionary<int, Action[]> actionsByIndex,
            List<Action> prepareActions,
            string clipKey,
            HashSet<string> compileStack)
        {
            foreach (NeoMember item in tracks)
            {
                if (item is not NeoMemberClass track)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' contains a non-Class child track row.");
                }
                string sourceChildId = ReadRequiredLookupId(
                    track,
                    "Child",
                    clipKey,
                    frameIndex: null);
                string childClipKey = ReadRequiredString(track, "ClipKey", clipKey);
                int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
                if (startFrame < 0)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' StartFrame must be non-negative; found {startFrame}.");
                }
                NeoMemberClass? placedChild = ResolvePlacedChild(
                    target.Client,
                    target.BackingNode,
                    sourceChildId,
                    clipKey,
                    $"child track '{childClipKey}'");
                if (placedChild is null)
                {
                    // Absent slot. Skipping before the fit check below is what
                    // excludes this track from `StartFrame + childLength <=
                    // Duration`: there is no child clip to fit. Every other
                    // track on this clip still compiles and plays.
                    continue;
                }
                if (string.IsNullOrWhiteSpace(placedChild.value?.id))
                {
                    throw MissingPlacementGraph(clipKey, $"child track '{childClipKey}'");
                }
                NeoGeneratedClassValue? childTarget =
                    target.Client.ResolveRegisteredGeneratedClassValue(placedChild.value!.id);
                if (childTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' cannot create a generated wrapper for placed child '{placedChild.value.id}'. Regenerate the project's C# types.");
                }
                NeoAnimationDefinition childDefinition = Compile(
                    childTarget,
                    childClipKey,
                    compileStack);
                int parentFrameLength = checked((int)Math.Ceiling(
                    childDefinition.Duration * (double)parentFps / childDefinition.FPS));
                if (startFrame + parentFrameLength > parentDuration)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' ends at parent frame {startFrame + parentFrameLength}, past Duration {parentDuration}.");
                }
                int lastAppliedChildFrame = -1;
                prepareActions.Add(() =>
                {
                    lastAppliedChildFrame = -1;
                    childDefinition.PreparePlayback();
                });
                for (int parentFrame = startFrame; parentFrame < parentDuration; parentFrame++)
                {
                    int elapsed = parentFrame - startFrame;
                    int childFrame = Math.Min(
                        childDefinition.Duration - 1,
                        (int)Math.Floor(elapsed * (double)childDefinition.FPS / parentFps));
                    int capturedChildFrame = childFrame;
                    AddFrameAction(
                        actionsByIndex,
                        parentFrame,
                        () =>
                        {
                            if (capturedChildFrame == lastAppliedChildFrame) return;
                            lastAppliedChildFrame = capturedChildFrame;
                            childDefinition.ApplyFrame(
                                capturedChildFrame,
                                useResolvedState: true);
                        });
                }
            }
        }

        /// <summary>
        /// Resolves the placed <c>Children</c> row an authored clip reference
        /// names, matching on <c>sourceValueId</c> exactly — name and index
        /// matching are deliberately unsupported.
        /// </summary>
        /// <returns>
        /// The matching row, or <c>null</c> when no row matches and at least one
        /// row carries provenance. A null return means the reference is for a
        /// slot this instance does not have — an optional slot on a subclass
        /// that trimmed its <c>Children</c> — and the caller skips that one
        /// reference. Ambiguous matches still throw, and so does a node on which
        /// NO row carries provenance, because those are data errors rather than
        /// absent slots.
        /// </returns>
        private static NeoMemberClass? ResolvePlacedChild(
            NeoClient client,
            NeoMemberClass target,
            string sourceChildId,
            string clipKey,
            string usage)
        {
            if (!target.TryGet("Children", out NeoMemberList? children))
            {
                throw MissingPlacementGraph(clipKey, usage);
            }
            NeoMemberClass? match = null;
            bool hasChildWithoutProvenance = false;
            bool hasChildWithProvenance = false;
            foreach (NeoMember child in children)
            {
                if (child is not NeoMemberClass childClass) continue;
                if (string.IsNullOrWhiteSpace(childClass.value?.sourceValueId))
                {
                    hasChildWithoutProvenance = true;
                    continue;
                }
                hasChildWithProvenance = true;
                if (!string.Equals(
                        childClass.value?.sourceValueId,
                        sourceChildId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (match is not null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' {usage} source child '{sourceChildId}' maps to multiple placed Children rows.");
                }
                match = childClass;
            }
            if (match is null)
            {
                // Only a node where NOT ONE row carries provenance is legacy
                // data. A mixed node is a normal P44 steady state — explicitly
                // authored rows carry no stamp, and the backfill leaves rows it
                // cannot structurally correspond unstamped — so it falls
                // through to the absent-slot skip below rather than failing the
                // whole clip with a migration message that would be untrue.
                if (hasChildWithoutProvenance && !hasChildWithProvenance)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' {usage} cannot run on legacy pre-0.7 placement '{target.value?.id ?? "<unmaterialized>"}': none of its Children rows carry sourceValueId placement-clone provenance. Migrate or recreate the persisted placement; re-exporting alone cannot upgrade saved placement rows.");
                }
                // Not a data error: the slot is simply absent on this instance.
                // Logged here, at compile time, so a clip looping at 8 FPS
                // reports once per reference rather than once per tick — and
                // deduped on the client, because the clip cache would otherwise
                // make it once per *instance* (fifty placements missing the same
                // slot, fifty warnings) and a nested child clip's compile
                // bypasses that cache entirely.
                if (client.ShouldReportAnimationChildSkip(clipKey, sourceChildId))
                {
                    UnityEngine.Debug.LogWarning(
                        $"Animation clip '{clipKey}' {usage} skipped: no placed Children row on placement '{target.value?.id ?? "<unmaterialized>"}' carries sourceValueId '{sourceChildId}'. The rest of the clip still plays.");
                }
                return null;
            }
            return match;
        }

        private static string ReadRequiredLookupId(
            NeoMemberClass node,
            string key,
            string clipKey,
            int? frameIndex)
        {
            string[]? selected = node.TryGet(key, out NeoMemberLookup? lookup)
                ? lookup.value?.value
                : null;
            if (selected is null
                || selected.Length != 1
                || string.IsNullOrWhiteSpace(selected[0]))
            {
                string frame = frameIndex.HasValue ? $" frame {frameIndex.Value}" : "";
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}'{frame} requires '{key}' to select exactly one authored child entry.");
            }
            return selected[0];
        }

        private static string ReadRequiredString(
            NeoMemberClass node,
            string key,
            string clipKey)
        {
            if (!node.TryGet(key, out NeoMemberString? value)
                || string.IsNullOrWhiteSpace(value.value?.value))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' is missing required String member '{key}'.");
            }
            return value.value!.value!;
        }

        private static void AddFrameAction(
            Dictionary<int, Action[]> actionsByIndex,
            int frameIndex,
            Action action)
        {
            if (!actionsByIndex.TryGetValue(frameIndex, out Action[] existing))
            {
                actionsByIndex[frameIndex] = new[] { action };
                return;
            }
            var combined = new Action[existing.Length + 1];
            Array.Copy(existing, combined, existing.Length);
            combined[existing.Length] = action;
            actionsByIndex[frameIndex] = combined;
        }

        private static Action[] CompileActions(
            NeoGeneratedClassValue target,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("Actions", out NeoMemberList? actions))
            {
                return Array.Empty<Action>();
            }
            var compiled = new List<Action>();
            foreach (NeoMember actionNode in actions)
            {
                if (actionNode is not NeoMemberFunctionRef functionRef
                    || string.IsNullOrWhiteSpace(functionRef.FunctionMemberId))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains an invalid FunctionRef action.");
                }
                string memberId = functionRef.FunctionMemberId!;
                compiled.Add(CompileAction(target, memberId, clipKey, frameIndex));
            }
            return compiled.ToArray();
        }

        private static Action CompileAction(
            NeoGeneratedClassValue target,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            if (string.IsNullOrWhiteSpace(target.classId))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} cannot resolve action '{memberId}' without a concrete target class.");
            }
            EnsureTargetActionMember(
                target.Client,
                target.classId!,
                memberId,
                clipKey,
                frameIndex);
            if (target.Client.TryResolveFunctionMember(memberId, out FunctionMember? native))
            {
                ValidateActionSignature(
                    native.returnTypeInfo,
                    native.argumentTypes,
                    native.deferred == true,
                    memberId,
                    clipKey,
                    frameIndex);
                return () => target.Client.InvokeNativeFunction(
                    memberId,
                    target,
                    Array.Empty<object?>());
            }
            NeoResolvedNSFunction? script = NeoNSFunctionRuntime.TryResolve(
                target.Client,
                memberId);
            if (script is null)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' does not resolve to a Function or NSFunction.");
            }
            ValidateActionSignature(
                script.ReturnTypeInfo,
                script.ArgumentTypes,
                script.Deferred,
                memberId,
                clipKey,
                frameIndex);
            if (string.IsNullOrWhiteSpace(target.valueId))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} NSFunction action '{memberId}' requires a materialized per-instance target value id.");
            }
            var function = new NeoMemberNSFunction(
                target.Client,
                script.Member,
                target.valueId,
                target.ValueOwnership);
            string targetValueId = target.valueId!;
            return () => function.Invoke(targetValueId, Array.Empty<object?>());
        }

        private static void ValidateActionSignature(
            TypeInfo returnType,
            FunctionArgumentTypeInfo[] arguments,
            bool deferred,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            if (returnType is not VoidTypeInfo
                || arguments.Length != 0
                || deferred)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' must be void-returning, zero-parameter, and non-deferred.");
            }
        }

        private static void EnsureTargetActionMember(
            NeoClient client,
            string targetClassId,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            foreach (MergedSchemaEntry entry in
                client.ResolveInstanceSurfaceSchema(targetClassId))
            {
                if (string.Equals(entry.memberId, memberId, StringComparison.Ordinal))
                {
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' is outside target class '{targetClassId}' merged schema.");
        }

        /// <summary>
        /// Builds the resolved-state chain each frame is entered with when
        /// traversal runs backward, wraps, or turns around in boomerang mode.
        ///
        /// <para>Per leaf (keyed by <see cref="NeoAnimationCompiledWrite.PathKey"/>,
        /// which is deliberately leaf-scoped) the chain carries <b>two</b>
        /// slots, so a field write and a whole-leaf write to the same path never
        /// alias each other:</para>
        /// <list type="bullet">
        /// <item>a <b>base</b> whole-leaf write — the prepare-time root snapshot
        /// until a frame writes the whole leaf, and that frame's write
        /// afterwards;</item>
        /// <item>a <b>pending field write</b> accumulating every field written
        /// since that base. A whole-leaf write clears it (it supersedes the
        /// fields it contains); a later field write for the same key supersedes
        /// only that key.</item>
        /// </list>
        ///
        /// <para>A leaf the clip only ever field-addresses gets NO whole-leaf
        /// root: its root is a field snapshot over exactly the keys the clip
        /// writes. That is what keeps a runtime-rebound sprite file alive across
        /// a pass — snapshotting the whole leaf here would re-assert the
        /// prepare-time <c>fileId</c> on every frame and freeze it.</para>
        /// </summary>
        internal static Dictionary<int, NeoAnimationCompiledWrite[]> ResolveFrames(
            int duration,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparse)
        {
            var leafOrder = new List<string>();
            var anchorByPath = new Dictionary<string, NeoAnimationCompiledWrite>(
                StringComparer.Ordinal);
            var wholeLeafPaths = new HashSet<string>(StringComparer.Ordinal);
            var fieldKeysByPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            var frameOrder = new List<int>(sparse.Keys);
            frameOrder.Sort();
            foreach (int frameIndex in frameOrder)
            {
                foreach (NeoAnimationCompiledWrite write in sparse[frameIndex])
                {
                    string pathKey = write.PathKey;
                    if (!anchorByPath.ContainsKey(pathKey))
                    {
                        anchorByPath[pathKey] = write;
                        leafOrder.Add(pathKey);
                    }
                    if (!write.IsFieldWrite)
                    {
                        wholeLeafPaths.Add(pathKey);
                        continue;
                    }
                    if (!fieldKeysByPath.TryGetValue(pathKey, out List<string>? keys))
                    {
                        keys = new List<string>();
                        fieldKeysByPath[pathKey] = keys;
                    }
                    foreach (NeoAnimationLeafFieldValue field in write.Fields)
                    {
                        if (!keys.Contains(field.Key)) keys.Add(field.Key);
                    }
                }
            }

            var baseByPath = new Dictionary<string, NeoAnimationCompiledWrite?>(
                StringComparer.Ordinal);
            var fieldByPath = new Dictionary<string, NeoAnimationCompiledWrite?>(
                StringComparer.Ordinal);
            foreach (string pathKey in leafOrder)
            {
                NeoAnimationCompiledWrite anchor = anchorByPath[pathKey];
                if (wholeLeafPaths.Contains(pathKey)
                    || !fieldKeysByPath.TryGetValue(pathKey, out List<string>? unionKeys))
                {
                    baseByPath[pathKey] = anchor.ResolveRoot();
                    fieldByPath[pathKey] = null;
                    continue;
                }
                baseByPath[pathKey] = null;
                fieldByPath[pathKey] = anchor.ResolveFieldRoot(unionKeys);
            }

            var resolved = new Dictionary<int, NeoAnimationCompiledWrite[]>();
            for (int frameIndex = 0; frameIndex < duration; frameIndex++)
            {
                if (sparse.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[]? writes))
                {
                    foreach (NeoAnimationCompiledWrite write in writes)
                    {
                        string pathKey = write.PathKey;
                        if (!write.IsFieldWrite)
                        {
                            baseByPath[pathKey] = write;
                            fieldByPath[pathKey] = null;
                            continue;
                        }
                        fieldByPath[pathKey] =
                            fieldByPath.TryGetValue(pathKey, out NeoAnimationCompiledWrite? pending)
                                && pending is not null
                                    ? pending.MergedWith(write)
                                    : write;
                    }
                }
                var ordered = new List<NeoAnimationCompiledWrite>();
                foreach (string pathKey in leafOrder)
                {
                    if (baseByPath.TryGetValue(pathKey, out NeoAnimationCompiledWrite? baseWrite)
                        && baseWrite is not null)
                    {
                        ordered.Add(baseWrite);
                    }
                    if (fieldByPath.TryGetValue(pathKey, out NeoAnimationCompiledWrite? fieldWrite)
                        && fieldWrite is not null)
                    {
                        ordered.Add(fieldWrite);
                    }
                }
                resolved[frameIndex] = ordered.ToArray();
            }
            return resolved;
        }

        internal static NeoValueWritePayload Payload(MemberValue? row)
        {
            object? value = row switch
            {
                null => null,
                NullMemberValue => null,
                BoolMemberValue typed => typed.value,
                NumberMemberValue typed => typed.value,
                StringMemberValue typed => typed.value,
                ArrayMemberValue typed => typed.value,
                FileMemberValue typed => typed.value,
                SpriteMemberValue typed => typed.value,
                Vector2MemberValue typed => typed.value,
                Vector3MemberValue typed => typed.value,
                ColorMemberValue typed => typed.value,
                // P42 §1.3 makes a structured leaf a path segment too, so the
                // old wording ("Animation Class values are path segments") is no
                // longer the whole rule. Both path-segment shapes say so here.
                ObjectMemberValue => throw new InvalidOperationException(
                    "Animation Class value rows are path segments, not leaf payloads: descend into the Class's keys."),
                PartialLeafMemberValue => throw new InvalidOperationException(
                    "Animation '$partial' structured-leaf rows are path segments into the leaf's fields, not leaf payloads: they are composed against the leaf's current value at apply time."),
                _ => throw new InvalidOperationException(
                    $"Unsupported animation payload row '{row.GetType().Name}'."),
            };
            return NeoValueWritePayload.FromValue(value);
        }

        private static int ReadRequiredInt(
            NeoMemberClass node,
            string key,
            string clipKey)
        {
            if (!node.TryGet(key, out NeoMemberInt? value)
                || value.value?.value is not double raw)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' is missing required Int member '{key}'.");
            }
            if (raw != Math.Truncate(raw))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' Int member '{key}' must be an integer; found {raw}.");
            }
            return checked((int)raw);
        }

        private static string? ResolveWorldKind(NeoClient client, string classId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = classId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (!client.TryGetClass(cursor!, out NeoSchemaClass? schemaClass)) return null;
                string? worldKind = schemaClass.system?["worldKind"]?.ToString();
                if (!string.IsNullOrWhiteSpace(worldKind)) return worldKind;
                cursor = schemaClass.extendsClassId;
            }
            return null;
        }

        private static string[] Append(string[] prefix, string value)
        {
            var result = new string[prefix.Length + 1];
            Array.Copy(prefix, result, prefix.Length);
            result[prefix.Length] = value;
            return result;
        }

        private static InvalidOperationException MissingPlacementGraph(
            string clipKey,
            string feature)
        {
            return new InvalidOperationException(
                $"Animation clip '{clipKey}' uses {feature}, but this export does not materialize a per-placement object graph with durable authored-child provenance. Re-export after the placement graph contract is available.");
        }
    }
}
