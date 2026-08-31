// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal sealed class NeoAnimationDefinition : IDisposable
    {
        private readonly IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites;
        private IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites;
        private readonly IReadOnlyDictionary<int, Action[]> actions;
        private readonly Action[] prepareActions;
        private readonly IDisposable[] disposables;
        private readonly HashSet<string> activePlaybackStack;
        private readonly string playbackKey;
        private readonly string playbackCycleLabel;
        private bool disposed;

        internal NeoAnimationDefinition(
            int fps,
            int duration,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites,
            IReadOnlyDictionary<int, Action[]> actions,
            Action[] prepareActions,
            IDisposable[] disposables,
            HashSet<string> activePlaybackStack,
            string playbackKey,
            string playbackCycleLabel)
        {
            FPS = fps;
            Duration = duration;
            this.sparseWrites = sparseWrites;
            this.resolvedWrites = resolvedWrites;
            this.actions = actions;
            this.prepareActions = prepareActions;
            this.disposables = disposables;
            this.activePlaybackStack = activePlaybackStack;
            this.playbackKey = playbackKey;
            this.playbackCycleLabel = playbackCycleLabel;
        }

        internal int FPS { get; }
        internal int Duration { get; }

        /// <summary>
        /// Releases everything the compile subscribed to. Today that is one
        /// <see cref="NeoAnimationSegmentSource"/> per segment track — each
        /// holds a <c>NeoClient.OnWritableValueChanged</c> handler for P48
        /// §3.1's re-resolution contract — plus every nested child clip
        /// definition this compile built, which own theirs.
        /// <para>Owned by the clip cache: <see cref="NeoClient"/> disposes a
        /// definition when the handle keyed to it is released or invalidated,
        /// which is the only lifetime the definition has.</para>
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (IDisposable disposable in disposables) disposable.Dispose();
        }

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
            if (!activePlaybackStack.Add(playbackKey))
            {
                throw new InvalidOperationException(
                    $"Animation child-track cycle reaches {playbackCycleLabel}.");
            }
            try
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
            finally
            {
                activePlaybackStack.Remove(playbackKey);
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
    /// <see cref="None"/> covers every other member kind — a <c>~partial</c>
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
    /// One validated field of a P42 <c>~partial</c> envelope, already checked
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
        /// P75 variant swaps make declarative answers virtual. A previously
        /// pinned row at the same stable path must therefore be removed before
        /// the imperative Apply closure runs. Structured-leaf partials share
        /// one persisted value row, so the destructive-confirmation unit is
        /// the leaf row rather than an individual component.
        ///
        /// <para>P75: the pin is located body-then-virtual, the same order
        /// every other member path resolves a bound child in. On a sparse root
        /// an imperative write does NOT materialize the key — it shadows the
        /// member's deterministic virtual id — so the pin to clear is that
        /// shadow, and searching the ownership graph for it would neither find
        /// it nor mean anything if it did.</para>
        /// </summary>
        internal void ClearInstanceOverride()
        {
            NeoMember leaf = ResolveLeafNode();
            string? valueId = leaf.overrideValueId ?? leaf.value?.id;
            if (string.IsNullOrEmpty(valueId)) return;
            string? parentValueId = writableParent.overrideValueId
                ?? writableParent.value?.id;
            bool detached = false;
            string? detachedValueId = null;
            if (string.IsNullOrEmpty(parentValueId)
                || !client.TryGetWritableValue(
                    writableParent.ownership,
                    parentValueId!,
                    out ObjectMemberValue? storedParent)
                || storedParent.value?.ContainsKey(writableKey) != true)
            {
                if (parentValueId is not null
                    && client.TryGetVirtualClassChildValueId(
                        parentValueId,
                        writableKey,
                        out string? virtualChildValueId)
                    && virtualChildValueId == valueId)
                {
                    // The pin is a shadow of the member's virtual id. There is
                    // no body key to detach; dropping the shadow is the clear.
                    storedParent = null;
                }
                else if (client.TryFindOwnedParent(
                        leaf.ownership,
                        valueId!,
                        out string? indexedParentId)
                    && client.TryGetWritableValue(
                        writableParent.ownership,
                        indexedParentId,
                        out storedParent))
                {
                    parentValueId = indexedParentId;
                }
                else
                {
                    storedParent = null;
                }
            }
            if (storedParent?.value is not null)
            {
                if (client.CloneRowForWrite(storedParent) is not ObjectMemberValue next
                    || next.value is not Dictionary<string, string> nextBody)
                {
                    throw new InvalidOperationException(
                        $"Variant override parent '{storedParent.id}' is not a writable Class row.");
                }
                if (nextBody.TryGetValue(writableKey, out detachedValueId))
                {
                    nextBody.Remove(writableKey);
                    client.SetWritableValue(writableParent.ownership, next, "value");
                    detached = true;
                }
            }
            if (detached)
            {
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    leaf.ownership,
                    detachedValueId!,
                    leaf.member);
            }
            else
            {
                client.RemoveWritableShadow(leaf.ownership, valueId!);
            }
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
            MemberValue? effective = writableParent.value is null
                ? null
                : client.ResolveClassChildRow(writableParent.value, writableKey);
            if (effective is not null
                && client.TryGetOverlaidValue(
                    leaf.ownership,
                    effective.id,
                    out MemberValue? row))
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

    /// <summary>
    /// A half-open crop window over content frames — P48 §2.3's second stage.
    /// <see cref="Start"/> is inclusive, <see cref="End"/> exclusive.
    /// </summary>
    internal readonly struct NeoAnimationCropWindow
    {
        internal NeoAnimationCropWindow(int start, int end)
        {
            Start = start;
            End = end;
        }

        internal int Start { get; }
        internal int End { get; }
        internal int Length => End - Start;
    }

    /// <summary>
    /// P48 §2.3's playback pipeline, in the four stages the spec names, shared
    /// by both track kinds:
    /// <code>
    /// resolve   content = dense frames of the scheduled thing over [0, BaseDuration)
    /// crop      window  = content[start, end)   start = OffsetStartIndex ?? 0
    ///                                           end   = OffsetEndIndex   ?? BaseDuration
    /// direct    play    = Direction == Forward ? window : reverse(window)
    /// schedule  clip frame f shows play[f - StartFrame] while that index is in
    ///           range; outside it, and past the owning clip's Duration, the
    ///           track writes nothing
    /// </code>
    ///
    /// <para>This is the .NET half of P48 acceptance 7 — the web's
    /// <c>src/models/animation/animation-playback.ts</c> is the same arithmetic
    /// over the same table (<c>animation-playback-parity-fixture.json</c>,
    /// vendored here as <c>NeoAnimationPlaybackParityFixture</c>). Track kind
    /// enters as a <b>rate</b> rather than as a discriminant:
    /// <paramref name="contentFramesPerClipFrame"/> is 1 for a segment track,
    /// which the owning clip's clock drives directly, and
    /// <c>childFps / parentFps</c> for a child clip track, which keeps its own
    /// clock. Cropping before scaling then falls out — the window is in content
    /// frames and the rate maps a clip-frame offset into it.</para>
    /// </summary>
    internal static class NeoAnimationPlayback
    {
        /// <summary>
        /// The one "this row contributes nothing at this frame" answer. It
        /// deliberately covers every reason at once — before
        /// <c>StartFrame</c>, past the window, past the clip's
        /// <c>Duration</c>, or a window that resolved empty — because the
        /// track's obligation is identical in all four: write nothing, and let
        /// the target member keep whatever it last held.
        /// </summary>
        internal const int WritesNothing = -1;

        /// <summary>
        /// Stage 2. False when the row can never play, which is a data state
        /// rather than an error: <c>BaseDuration</c> is resolved content, so
        /// P48 §2.3 clamps crop bounds against it at runtime. An authored
        /// window that is empty or inverted is rejected earlier, at
        /// <see cref="NeoAnimationCompiler.ValidateProject"/>.
        /// </summary>
        internal static bool TryCropWindow(
            int baseDuration,
            int? offsetStartIndex,
            int? offsetEndIndex,
            out NeoAnimationCropWindow window)
        {
            int length = Math.Max(0, baseDuration);
            int start = ClampIndex(offsetStartIndex ?? 0, length);
            int end = ClampIndex(offsetEndIndex ?? length, length);
            if (end <= start)
            {
                window = default;
                return false;
            }
            window = new NeoAnimationCropWindow(start, end);
            return true;
        }

        /// <summary>
        /// Stages 3 and 4. The <b>content</b> index a row plays at one frame of
        /// the owning clip, or <see cref="WritesNothing"/>.
        /// </summary>
        internal static int ContentIndexAtClipFrame(
            int clipDuration,
            int clipFrame,
            int startFrame,
            double contentFramesPerClipFrame,
            NeoPlayDirection direction,
            in NeoAnimationCropWindow window)
        {
            if (clipFrame < 0) return WritesNothing;
            if (clipFrame >= clipDuration) return WritesNothing;
            int offset = clipFrame - startFrame;
            if (offset < 0) return WritesNothing;
            int playIndex = (int)Math.Floor(offset * contentFramesPerClipFrame);
            if (playIndex >= window.Length) return WritesNothing;
            return direction == NeoPlayDirection.Forward
                ? window.Start + playIndex
                : window.End - 1 - playIndex;
        }

        /// <summary>
        /// The content frame rate for a child clip track: the child's own clock
        /// read against the parent's. Throws rather than clamping — an
        /// unplayable clock is a document defect the author should see, not a
        /// frame to guess at.
        /// </summary>
        internal static double ChildClipContentFrameRate(
            int childFps,
            int parentFps,
            string label)
        {
            if (childFps < 1)
            {
                throw new InvalidOperationException(
                    $"{label} child clip FPS must be at least 1 to schedule the clip; found {childFps}.");
            }
            if (parentFps < 1)
            {
                throw new InvalidOperationException(
                    $"{label} parent clip FPS must be at least 1 to schedule a child clip; found {parentFps}.");
            }
            return childFps / (double)parentFps;
        }

        private static int ClampIndex(int value, int baseDuration)
        {
            return Math.Min(baseDuration, Math.Max(0, value));
        }
    }

    /// <summary>
    /// The deferred write source P48 §4 requires for a segment track: the
    /// compiled definition holds the track <b>row</b>, not expanded values, and
    /// re-reads the resolved segment whenever a write may have moved it.
    ///
    /// <para>P48 §3.1's contract is that the resolved segment is a function of
    /// the instance's current state, evaluated per applied frame — "an equip
    /// mid-animation must change the sprite on the next frame". Memoization is
    /// legal only when invisible, so the dirty flag here is deliberately
    /// <b>conservative</b>: any writable value change anywhere marks the source
    /// dirty and the next applied frame re-resolves. A narrower key would have
    /// to be the getter's read set, which the .NET evaluator does not report
    /// (unlike the compiler that derives dialogue linked values), and a
    /// narrower key that is wrong silently breaks the one property the whole
    /// design leans on.</para>
    ///
    /// <para>What the flag still buys: a clip whose frame wrote nothing pays
    /// nothing, and a re-resolution whose segment row is unchanged reuses the
    /// node tree rather than rebuilding it — so an equip costs a rebuild and a
    /// steady frame costs two member reads.</para>
    /// </summary>
    internal sealed class NeoAnimationSegmentSource : IDisposable
    {
        private readonly NeoClient client;
        private readonly NeoMemberClass track;
        private readonly string segmentKey;
        private readonly string label;
        private readonly string? trackValueId;
        private readonly Action<NeoValueOwnership, string> writableValueChanged;

        private MemberValue?[] contentRows = Array.Empty<MemberValue?>();
        private bool[] contentAuthored = Array.Empty<bool>();
        private bool dirty = true;
        private bool disposed;

        internal NeoAnimationSegmentSource(
            NeoClient client,
            NeoMemberClass track,
            string segmentKey,
            string label)
        {
            this.client = client;
            this.track = track;
            this.segmentKey = segmentKey;
            this.label = label;
            trackValueId = track.value?.id;
            writableValueChanged = HandleWritableValueChanged;
            client.OnWritableValueChanged += writableValueChanged;
        }

        /// <summary>
        /// The resolved segment's own length in its own frames — P48 §2.1's
        /// <c>BaseDuration</c> for a segment track. Zero when the segment
        /// resolves to nothing (an unequipped lookup, a getter that failed, a
        /// row that is not a segment), which makes every crop window empty and
        /// is exactly §3.2's "the track writes nothing".
        /// </summary>
        internal int BaseDuration
        {
            get
            {
                EnsureResolved();
                return contentRows.Length;
            }
        }

        /// <summary>
        /// The value row one content index plays. False means no segment frame
        /// has been authored at or before that index — which is <b>not</b>
        /// "write null": a segment whose first row sits at index 2 genuinely
        /// has nothing to say about indices 0 and 1, while a row that authored
        /// a null <c>Value</c> writes null (P42 §6's null-leaf rule applied to
        /// a new writer).
        /// </summary>
        internal bool TryReadContent(int index, out MemberValue? row)
        {
            EnsureResolved();
            if (index < 0 || index >= contentRows.Length)
            {
                row = null;
                return false;
            }
            row = contentRows[index];
            return contentAuthored[index];
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.OnWritableValueChanged -= writableValueChanged;
            contentRows = Array.Empty<MemberValue?>();
            contentAuthored = Array.Empty<bool>();
        }

        private void HandleWritableValueChanged(
            NeoValueOwnership ownership,
            string valueId)
        {
            if (disposed) return;
            dirty = true;
        }

        private void EnsureResolved()
        {
            if (disposed || !dirty) return;
            dirty = false;
            contentRows = Array.Empty<MemberValue?>();
            contentAuthored = Array.Empty<bool>();
            string? rowId = ResolveSegmentRowId();
            if (rowId is null) return;
            ReadContent(rowId);
        }

        /// <summary>
        /// The member the played row's class binds under the Segment schema
        /// key — resolved fresh per re-resolution because the row's concrete
        /// class decides which implementation shape answers.
        /// </summary>
        private Member? ResolveSegmentMember(string classId)
        {
            foreach (MergedSchemaEntry entry in
                client.ResolveInstanceSurfaceSchema(classId))
            {
                if (!string.Equals(entry.schemaKey, segmentKey, StringComparison.Ordinal))
                {
                    continue;
                }
                if (client.TryGetMember(entry.memberId, out Member? member))
                {
                    return member;
                }
                return null;
            }
            return null;
        }

        /// <summary>
        /// P48 §2.2's three implementation shapes, resolved to the segment
        /// <b>row</b> each of them ends at: a stored value is the row the
        /// schema key binds, a lookup is the row its first selected id names,
        /// and a getter is the row its result carries a value id for. A getter
        /// that synthesizes a record no row backs resolves to nothing —
        /// acceptable, because a synthesized segment has no frame rows to read
        /// either.
        /// </summary>
        private string? ResolveSegmentRowId()
        {
            // Rows and members are read from the client's value store rather
            // than through the compile-time node tree: nodes built standalone
            // by the compiler do not reliably re-resolve their values after a
            // registry-affecting write, and this source's whole contract is
            // re-resolution after writes (P48 §3.1). Only the getter shape
            // still touches a node, because Compute takes an explicit receiver
            // id and never reads the node's own cached value.
            if (string.IsNullOrWhiteSpace(trackValueId)) return null;
            if (client.ResolveValueRow(trackValueId!) is not ObjectMemberValue trackRow
                || string.IsNullOrWhiteSpace(trackRow.classId))
            {
                return null;
            }
            Member? segmentMember = ResolveSegmentMember(trackRow.classId!);
            switch (segmentMember)
            {
                case LookupMember:
                {
                    if (client.ResolveClassChildRow(trackRow, segmentKey)
                            is not ArrayMemberValue lookupRow
                        || lookupRow.value is null
                        || lookupRow.value.Length == 0)
                    {
                        return null;
                    }
                    string first = lookupRow.value[0];
                    return string.IsNullOrWhiteSpace(first) ? null : first;
                }
                case NSPropertyMember:
                {
                    if (!track.TryGet(segmentKey, out NeoMemberNSProperty? getter))
                    {
                        return null;
                    }
                    NeoScript.NSGetterResult result = getter.Compute(trackValueId!);
                    if (!result.ok)
                    {
                        // A getter error is absence, not a crash: §3.2 makes an
                        // unresolvable segment silent and legal at runtime, and
                        // throwing here would take down a clip mid-frame.
                        if (client.ShouldReportAnimationApplySkip(
                                $"{label}$segmentGetter"))
                        {
                            UnityEngine.Debug.LogWarning(
                                $"{label} Segment getter failed, so the track writes nothing: {result.error}");
                        }
                        return null;
                    }
                    string? valueId = NeoGeneratedTypesSupport.ValueId(result.value);
                    return string.IsNullOrWhiteSpace(valueId) ? null : valueId;
                }
                case ClassMember:
                {
                    return client.ResolveClassChildRow(trackRow, segmentKey)?.id;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// P48 §2.3 stage 1 for a segment: sparse rows addressed by
        /// <c>Index</c>, each held until the next authored row or the end of
        /// <c>Duration</c>. Read straight from the client's effective rows so
        /// a re-resolution after any write sees current state; rows are read
        /// by <c>Index</c> rather than by list order, and a duplicate index is
        /// not defended against — push rejects duplicates, and silently
        /// preferring one would hide it.
        /// </summary>
        /// <summary>
        /// The effective row bound to <paramref name="schemaKey"/> on a class
        /// row: the stored body first, then the P75 virtual index.
        ///
        /// <para>Segments and their frames are ordinary class instances, so a
        /// collapse-stamped one omits every member its construction supplied
        /// and nothing overrode. Reading the body alone drops exactly those —
        /// a missing <c>Duration</c> abandons the whole segment and a missing
        /// <c>Value</c> reads as an authored null, which actively clears the
        /// channel rather than being ignored.</para>
        /// </summary>
        private MemberValue? ResolveClassMemberRow(
            ObjectMemberValue row,
            string schemaKey)
        {
            return client.ResolveClassChildRow(row, schemaKey);
        }

        private void ReadContent(string rowId)
        {
            if (client.ResolveValueRow(rowId) is not ObjectMemberValue segmentRow)
            {
                return;
            }
            if (ResolveClassMemberRow(segmentRow, "Duration") is not NumberMemberValue durationRow
                || durationRow.value is not double rawDuration)
            {
                return;
            }
            int duration = Math.Max(0, (int)Math.Floor(rawDuration));
            if (duration == 0) return;

            var rows = new MemberValue?[duration];
            var authored = new bool[duration];
            if (ResolveClassMemberRow(segmentRow, "Frames") is ArrayMemberValue framesRow
                && framesRow.value is not null)
            {
                var ordered = new List<(int index, MemberValue? row)>();
                foreach (string frameId in framesRow.value)
                {
                    if (string.IsNullOrWhiteSpace(frameId)) continue;
                    if (client.ResolveValueRow(frameId) is not ObjectMemberValue frameRow)
                    {
                        continue;
                    }
                    if (ResolveClassMemberRow(frameRow, "Index") is not NumberMemberValue indexRow
                        || indexRow.value is not double rawIndex)
                    {
                        continue;
                    }
                    int index = (int)Math.Floor(rawIndex);
                    if (index < 0 || index >= duration) continue;
                    ordered.Add((index, ResolveClassMemberRow(frameRow, "Value")));
                }
                ordered.Sort((left, right) => left.index.CompareTo(right.index));
                foreach ((int index, MemberValue? row) in ordered)
                {
                    for (int position = index; position < duration; position++)
                    {
                        rows[position] = row;
                        authored[position] = true;
                    }
                }
            }
            contentRows = rows;
            contentAuthored = authored;
        }
    }

    internal static class NeoAnimationCompiler
    {
        private const string AnimationClipWorldKind = "animationClip";
        private const string AnimationChildTrackWorldKind = "animationChildTrack";
        private const string AnimationSegmentTrackWorldKind = "animationSegmentTrack";
        private const string AnimationSegmentWorldKind = "animationSegment";
        private const string SegmentSchemaKey = "Segment";

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
                        if (item is not NeoMemberClass declarationFrame)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class frame row.");
                        }
                        using var validation = NeoGeneratedTypesSupport
                            .TryResolveDeclarationForValidation(
                                client,
                                declarationFrame);
                        if (validation is null) continue;
                        NeoMemberClass frame = validation.Value;
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
                        if (item is not NeoMemberClass declarationTrack)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class track row.");
                        }
                        using var validation = NeoGeneratedTypesSupport
                            .TryResolveDeclarationForValidation(
                                client,
                                declarationTrack);
                        if (validation is null) continue;
                        ValidateExportTrack(
                            client,
                            validation.Value,
                            clipKey,
                            duration,
                            validated,
                            stack);
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
        /// Load-time validation of one <c>Tracks</c> row, dispatched by the
        /// row's own class (P48 §2.2). Mirrors the web's
        /// <c>AnimationValidationContext.validateTracks</c>, message for
        /// message, so an author sees the same diagnostic from a push and from
        /// a client load.
        ///
        /// <para>P48 §2.3 <b>deletes</b> P29's fit error: content that runs
        /// past the owning clip's end truncates, because clipping is what a
        /// clip does, and the compositions that error forbade (the two-row
        /// yoyo) are exactly the ones the crop window exists to express. What
        /// survives is a row that can never play at all — a <c>StartFrame</c>
        /// outside the clip, or a crop window the author wrote empty or
        /// inverted. Crop bounds against the <i>resolved</i> content are
        /// runtime-clamped instead, since a lookup-backed segment's length is
        /// instance data this pass does not have.</para>
        /// </summary>
        private static void ValidateExportTrack(
            NeoClient client,
            NeoMemberClass track,
            string clipKey,
            int duration,
            HashSet<string> validated,
            HashSet<string> stack)
        {
            NeoAnimationTrackKind kind = ResolveTrackKind(track);
            string label = kind == NeoAnimationTrackKind.Segment
                ? $"Animation clip '{clipKey}' segment track '{track.value?.id ?? "<unmaterialized>"}'"
                : $"Animation clip '{clipKey}' child track '{track.value?.id ?? "<unmaterialized>"}'";
            if (kind == NeoAnimationTrackKind.Unknown)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' track row '{track.value?.id ?? "<unmaterialized>"}' has class '{TrackClassName(client, track)}', which is neither a child clip track nor a segment track.");
            }

            _ = ValidateSelector(track, label);
            ReadSelectorRefresh(track, label);

            int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
            if (startFrame < 0)
            {
                throw new InvalidOperationException(
                    $"{label} StartFrame {startFrame} is negative.");
            }
            if (startFrame >= duration)
            {
                throw new InvalidOperationException(
                    $"{label} StartFrame {startFrame} is at or past the owning clip's Duration {duration}, so the row can never play.");
            }
            ReadTrackDirection(track, label);
            ReadTrackCropWindow(track, label);

            if (kind == NeoAnimationTrackKind.Segment)
            {
                ValidateExportSegmentTrack(client, track, childClass: null, label);
                return;
            }

            string childClipKey = ReadRequiredString(track, "ClipKey", clipKey);
            // A selector may be dynamic, so the child class and its nested
            // clip cannot be known from the exported declaration alone. The
            // selected instance is validated (and the nested clip compiled)
            // when the runtime evaluates the selector.
            _ = childClipKey;
        }

        /// <summary>
        /// P48 §7's target rule, as much of it as this runtime can answer: the
        /// concrete track class must name a target member, that member must
        /// exist on the played child's class, and the abstract <c>Segment</c>
        /// declaration must be implemented by something the player can read.
        ///
        /// <para>The generic-binding half of the web's rule (the target's kind
        /// equals the bound <c>TValue</c>, and the child descends from the
        /// bound <c>TChild</c>) stays at push: those are statements about the
        /// class declaration, which push validates against the whole document
        /// and which the SDK receives already-checked. What the SDK adds is the
        /// instance half push cannot see — the child this row actually
        /// names.</para>
        /// </summary>
        private static void ValidateExportSegmentTrack(
            NeoClient client,
            NeoMemberClass track,
            NeoSchemaClass? childClass,
            string label)
        {
            string trackClassName = TrackClassName(client, track);
            string? targetMemberId = ResolveTargetMemberId(client, TrackClassId(track));
            if (targetMemberId is null)
            {
                throw new InvalidOperationException(
                    $"{label} class '{trackClassName}' declares no target member, so it has nothing to write.");
            }
            MergedSchemaEntry? targetEntry = null;
            if (childClass is not null)
            {
                foreach (MergedSchemaEntry entry in
                    client.ResolveInstanceSurfaceSchema(childClass.id))
                {
                    if (MemberDescendsFrom(client, entry.memberId, targetMemberId))
                    {
                        targetEntry = entry;
                        break;
                    }
                }
            }
            if (childClass is not null && targetEntry is null)
            {
                throw new InvalidOperationException(
                    $"{label} class '{trackClassName}' targets member '{targetMemberId}', which '{childClass.name}' does not declare.");
            }
            string effectiveTargetMemberId = targetEntry?.memberId ?? targetMemberId;
            if (!client.TryGetMember(effectiveTargetMemberId, out Member? targetMember))
            {
                throw new InvalidOperationException(
                    $"{label} target member '{effectiveTargetMemberId}' is not in this project.");
            }
            if (!track.TryGet(SegmentSchemaKey, out NeoMember? segment))
            {
                throw new InvalidOperationException(
                    $"{label} class '{trackClassName}' does not implement the abstract Segment member, so nothing resolves to play.");
            }
            if (segment is not (NeoMemberClass or NeoMemberLookup or NeoMemberNSProperty))
            {
                throw new InvalidOperationException(
                    $"{label} implements Segment as a {segment.member.kind} member; P48 §2.2 accepts a stored value, a lookup, or a getter.");
            }
            // A stored or lookup implementation names a class the runtime can
            // check; a getter's return type is checked at push, where the
            // NeoScript type checker is.
            if (segment is NeoMemberClass storedSegment
                && !ClassInheritsWorldKind(
                    client,
                    storedSegment.value?.classId ?? storedSegment.member.classId,
                    AnimationSegmentWorldKind))
            {
                throw new InvalidOperationException(
                    $"{label} implements Segment with a value whose class is not an animation segment.");
            }
            if (targetMember.kind == MemberKind.Class
                || targetMember.kind == MemberKind.List
                || targetMember.kind == MemberKind.Dictionary)
            {
                throw new InvalidOperationException(
                    $"{label} targets '{childClass?.name ?? trackClassName}.{targetEntry?.schemaKey ?? targetMember.name}' of kind {targetMember.kind}, which is a container rather than a value a segment frame can write.");
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
                            $"{where} carries a '~partial' field envelope, but that member has no addressable fields. Only Sprite, Vector2(Int), Vector3(Int), and Color members do.");
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
                || child.member.Mutability == NeoMemberMutabilityKind.ReadOnly
                || child.member.Modifier == NeoMemberModifierKind.Static)
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
            if (client.ResolveValueRow(overrideValueId) is not ObjectMemberValue) return;
            throw new InvalidOperationException(
                $"{where} addresses a field path deeper than one level. Structured leaves are one level deep: a '~partial' envelope names fields, never sub-fields.");
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
        /// <c>defaultValue</c>, then the retained member-level
        /// <c>member.valueId</c> row.
        /// </summary>
        private static NeoAnimationDefinitionPresence ResolveDefinitionPresence(
            NeoClient client,
            ObjectMemberValue? targetRow,
            string schemaKey,
            Member declaration,
            out ObjectMemberValue? record)
        {
            record = null;
            bool hasStoredMapping = targetRow?.value?.ContainsKey(schemaKey) == true;
            MemberValue? mapped = targetRow is null
                ? null
                : client.ResolveClassChildRow(targetRow, schemaKey);
            if (mapped is not null)
            {
                if (mapped.IsRemoved)
                {
                    return NeoAnimationDefinitionPresence.NullValue;
                }
                record = mapped as ObjectMemberValue;
                return RowPresence(mapped);
            }
            if (hasStoredMapping)
            {
                return NeoAnimationDefinitionPresence.NullValue;
            }
            NeoAnimationDefinitionPresence declared = DeclarationDefaultPresence(
                declaration,
                out record);
            if (declared != NeoAnimationDefinitionPresence.Unknown) return declared;
            if (!string.IsNullOrWhiteSpace(declaration.valueId))
            {
                MemberValue? memberValue = client.ResolveValueRow(declaration.valueId!);
                if (memberValue is null || memberValue.IsRemoved)
                {
                    return NeoAnimationDefinitionPresence.NullValue;
                }
                record = memberValue as ObjectMemberValue;
                return RowPresence(memberValue);
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
                        native.Dispatch == NeoFunctionDispatchKind.Asynchronous,
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
                string label =
                    $"Animation clip '{clipKey}' frame {frameIndex} child override '{childOverride.value?.id ?? "<unmaterialized>"}'";
                ValidateSelector(childOverride, label);
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                ValidateExportOverrides(
                    client,
                    overrides,
                    targetRow: null,
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
            return Compile(
                target,
                schemaKey,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static NeoAnimationDefinition Compile(
            NeoGeneratedClassValue target,
            string schemaKey,
            HashSet<string> compileStack,
            HashSet<string> activePlaybackStack)
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
                var disposables = new List<IDisposable>();
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
                        var selectorActions = new List<Action>();
                        if (frame.TryGet("ChildOverrides", out NeoMemberList? childOverrides))
                        {
                            CompileChildOverrides(
                                target,
                                childOverrides,
                                writes,
                                selectorActions,
                                schemaKey,
                                frameIndex);
                        }
                        sparseByIndex[frameIndex] = writes;
                        Action[] authoredActions = CompileActions(
                            target,
                            frame,
                            schemaKey,
                            frameIndex);
                        selectorActions.AddRange(authoredActions);
                        actionsByIndex[frameIndex] = selectorActions.ToArray();
                    }
                }
                try
                {
                    if (clipNode.TryGet("Tracks", out NeoMemberList? tracks))
                    {
                        CompileTracks(
                            target,
                            tracks,
                            fps,
                            duration,
                            actionsByIndex,
                            prepareActions,
                            disposables,
                            schemaKey,
                            compileStack,
                            activePlaybackStack);
                    }
                }
                catch
                {
                    // A track that throws half-way through the list leaves the
                    // earlier tracks' subscriptions with no definition to own
                    // them; nothing else will ever dispose them.
                    foreach (IDisposable disposable in disposables) disposable.Dispose();
                    throw;
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
                    prepareActions.ToArray(),
                    disposables.ToArray(),
                    activePlaybackStack,
                    compileKey,
                    $"clip '{schemaKey}' on value '{target.valueId ?? target.classId}'");
            }
            finally
            {
                compileStack.Remove(compileKey);
            }
        }

        /// <summary>
        /// P67 §7.2 reuses this verbatim for a variant's declarative halves,
        /// which is why it is internal rather than private: a variant applies
        /// the same `Partial&lt;T&gt;` semantics to the same kind of target, and
        /// a second partial-application write path would be a second set of
        /// eligibility and descent rules to keep in step.
        /// </summary>
        internal static void FlattenOverrides(
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
                // override row is a `~partial` envelope, and a write target when
                // it is a full value. The envelope decides which.
                NeoAnimationLeafKind leafKind = NeoAnimationLeafFields.KindOf(child.member);
                NeoPartialLeafValue? partialFields = child.partialLeafValue?.value;
                if (partialFields is not null)
                {
                    if (leafKind == NeoAnimationLeafKind.None)
                    {
                        throw new InvalidOperationException(
                            $"{where} carries a '~partial' field envelope, but that member has no addressable fields. Only Sprite, Vector2(Int), Vector3(Int), and Color members do.");
                    }
                    // Eligibility is evaluated on the ENCLOSING leaf member, not
                    // on the field: fields carry no storage of their own and a
                    // field segment has no member id to resolve.
                    EnsureEligibleCompiledLeaf(child, ownership, where);
                    List<NeoAnimationLeafFieldValue> compiledFields =
                        NeoAnimationLeafFields.Compile(leafKind, partialFields, where);
                    // `{"~partial":{}}` is the wire form of "no change", so it
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
                || child.member.Mutability == NeoMemberMutabilityKind.ReadOnly
                || child.member.Modifier == NeoMemberModifierKind.Static)
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
                || client.ResolveValueRow(assetValueId) is not ObjectMemberValue asset
                || client.ResolveClassChildRow(target.value, path[0])
                    is not MemberValue placedChild
                || client.ResolveClassChildRow(asset, path[0])
                    is not MemberValue authoredChild
                || !string.Equals(placedChild.id, authoredChild.id, StringComparison.Ordinal))
            {
                return;
            }
            throw new InvalidOperationException(
                $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' still references shared authored row '{authoredChild.id}' on placement '{target.value.id}'. Re-export with a placement-owned clone carrying sourceValueId before playback.");
        }

        /// <summary>
        /// Resolves a P60 animation selector against the owner whose clip is
        /// playing. OnLoad caches the first result for the definition's
        /// lifetime; PerFrame deliberately re-enters the evaluator.
        /// </summary>
        private sealed class NeoAnimationSelector
        {
            private readonly NeoGeneratedClassValue target;
            private readonly NeoMemberDelegate selector;
            private readonly string label;
            private NeoMemberClass? cached;
            private bool hasResolved;

            internal NeoAnimationSelector(
                NeoGeneratedClassValue target,
                NeoMemberClass selectorOwner,
                string label)
            {
                this.target = target;
                this.label = label;
                selector = ValidateSelector(selectorOwner, label);
                Refresh = ReadSelectorRefresh(selectorOwner, label);
            }

            internal NeoSelectorRefreshKind Refresh { get; }

            internal NeoMemberClass Resolve()
            {
                if (Refresh == NeoSelectorRefreshKind.OnLoad && hasResolved)
                {
                    return cached!;
                }
                if (string.IsNullOrWhiteSpace(target.valueId))
                {
                    throw new InvalidOperationException(
                        $"{label} cannot evaluate its selector without a materialized animation owner value id.");
                }
                try
                {
                    NeoConstructorValueReference? selected =
                        selector.InvokeValueReference(
                            target.valueId!,
                            target.ValueOwnership);
                    if (!selected.HasValue)
                    {
                        throw new InvalidOperationException(
                            "selector returned null or a value that is not a stored class row");
                    }
                    NeoMemberClass child = ResolveSelectedChild(
                        target,
                        selected.Value.valueId,
                        label);
                    if (Refresh == NeoSelectorRefreshKind.OnLoad)
                    {
                        cached = child;
                        hasResolved = true;
                    }
                    return child;
                }
                catch (Exception error) when (error is not InvalidOperationException
                    || !error.Message.StartsWith(label, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{label} selector failed: {error.Message}",
                        error);
                }
            }
        }

        private static NeoMemberClass ResolveSelectedChild(
            NeoGeneratedClassValue target,
            string selectedValueId,
            string label)
        {
            if (!target.BackingNode.TryGet("Children", out NeoMemberList? children))
            {
                throw new InvalidOperationException(
                    $"{label} cannot resolve a selector because the animation owner has no Children list.");
            }
            foreach (NeoMember item in children)
            {
                if (item is NeoMemberClass child
                    && (string.Equals(
                            child.value?.id,
                            selectedValueId,
                            StringComparison.Ordinal)
                        || string.Equals(
                            child.overrideValueId,
                            selectedValueId,
                            StringComparison.Ordinal)))
                {
                    return child;
                }
            }
            throw new InvalidOperationException(
                $"{label} selector returned child '{selectedValueId}' outside the animation owner's Children graph.");
        }

        /// <summary>
        /// Shared with P67 variants (§7.2). `clipKey` and `frameIndex` are
        /// message and dedupe keys only; a variant passes its own id and 0.
        /// </summary>
        internal static void CompileChildOverrides(
            NeoGeneratedClassValue target,
            NeoMemberList childOverrides,
            List<NeoAnimationCompiledWrite> writes,
            List<Action> selectorActions,
            string clipKey,
            int frameIndex,
            bool resolveSelectorsImmediately = false)
        {
            foreach (NeoMember item in childOverrides)
            {
                if (item is not NeoMemberClass childOverride)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains a non-Class child override row.");
                }
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                string label =
                    $"Animation clip '{clipKey}' frame {frameIndex} child override '{childOverride.value?.id ?? "<unmaterialized>"}'";
                var selector = new NeoAnimationSelector(
                    target,
                    childOverride,
                    label);
                if (selector.Refresh == NeoSelectorRefreshKind.OnLoad
                    || resolveSelectorsImmediately)
                {
                    NeoMemberClass placedChild = selector.Resolve();
                    FlattenOverrides(
                        target.Client,
                        placedChild,
                        overrides,
                        Array.Empty<string>(),
                        placedChild.ownership,
                        writes,
                        clipKey,
                        frameIndex);
                    continue;
                }
                selectorActions.Add(() =>
                {
                    NeoMemberClass placedChild = selector.Resolve();
                    var selectedWrites = new List<NeoAnimationCompiledWrite>();
                    FlattenOverrides(
                        target.Client,
                        placedChild,
                        overrides,
                        Array.Empty<string>(),
                        placedChild.ownership,
                        selectedWrites,
                        clipKey,
                        frameIndex);
                    foreach (NeoAnimationCompiledWrite write in selectedWrites)
                    {
                        write.Apply();
                    }
                });
            }
        }

        /// <summary>
        /// P48 §2.2 / §4 — <c>NeoAnimationClip.Tracks</c> holds
        /// <c>NeoAnimationTrackBase</c> rows, so a row's kind is a property of
        /// the row rather than of the list. Everything the base declares
        /// (<c>Selector</c>, <c>StartFrame</c>, <c>Direction</c>, the crop window)
        /// is read once here; the per-kind compiles below see an already-read
        /// schedule.
        ///
        /// <para>Both kinds compile into the <b>same</b> per-frame action
        /// stream, appended while iterating <c>Tracks</c> in list order, so
        /// §2.3's "if two rows write the same member at the same frame, apply
        /// order is <c>Tracks</c> list order, last write wins" falls out of
        /// execution order rather than needing a rule of its own. And because
        /// <see cref="NeoAnimationDefinition.ApplyFrame"/> runs all writes then
        /// all actions, a track always applies after the owning frame's own
        /// overrides — which is exactly the web's
        /// <c>resolveAnimationClipContent</c> fold.</para>
        /// </summary>
        private static void CompileTracks(
            NeoGeneratedClassValue target,
            NeoMemberList tracks,
            int parentFps,
            int parentDuration,
            Dictionary<int, Action[]> actionsByIndex,
            List<Action> prepareActions,
            List<IDisposable> disposables,
            string clipKey,
            HashSet<string> compileStack,
            HashSet<string> activePlaybackStack)
        {
            foreach (NeoMember item in tracks)
            {
                if (item is not NeoMemberClass track)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' contains a non-Class track row.");
                }
                NeoAnimationTrackKind kind = ResolveTrackKind(track);
                string label = kind == NeoAnimationTrackKind.Segment
                    ? $"Animation clip '{clipKey}' segment track '{track.value?.id ?? "<unmaterialized>"}'"
                    : $"Animation clip '{clipKey}' child track '{track.value?.id ?? "<unmaterialized>"}'";
                if (kind == NeoAnimationTrackKind.Unknown)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' track row '{track.value?.id ?? "<unmaterialized>"}' is neither a child clip track nor a segment track.");
                }
                string? childClipKey = kind == NeoAnimationTrackKind.ChildClip
                    ? ReadRequiredString(track, "ClipKey", clipKey)
                    : null;
                var selector = new NeoAnimationSelector(
                    target,
                    track,
                    label);
                int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
                if (startFrame < 0)
                {
                    throw new InvalidOperationException(
                        $"{label} StartFrame {startFrame} is negative.");
                }
                if (startFrame >= parentDuration)
                {
                    throw new InvalidOperationException(
                        $"{label} StartFrame {startFrame} is at or past the owning clip's Duration {parentDuration}, so the row can never play.");
                }
                NeoPlayDirection direction = ReadTrackDirection(track, label);
                (int? offsetStart, int? offsetEnd) = ReadTrackCropWindow(track, label);

                if (kind == NeoAnimationTrackKind.Segment)
                {
                    CompileSegmentTrack(
                        target.Client,
                        track,
                        selector,
                        startFrame,
                        direction,
                        offsetStart,
                        offsetEnd,
                        parentDuration,
                        actionsByIndex,
                        disposables,
                        label);
                    continue;
                }

                CompileChildClipTrack(
                    target,
                    childClipKey!,
                    selector,
                    startFrame,
                    direction,
                    offsetStart,
                    offsetEnd,
                    parentFps,
                    parentDuration,
                    actionsByIndex,
                    prepareActions,
                    disposables,
                    label,
                    compileStack,
                    activePlaybackStack);
            }
        }

        /// <summary>
        /// P29's child clip track, now under P48 §2.1's authored playback: the
        /// crop window applies in the <b>child's</b> frame space before the
        /// existing fps scaling, and <c>Reverse</c> maps
        /// <c>t → (D − 1) − t</c> over the child's <b>resolved</b> timeline.
        /// Nested content follows for free — the child is applied through
        /// <c>ApplyFrame(..., useResolvedState: true)</c>, whose resolved
        /// frames already carry the hold rule and whose actions already carry
        /// the child's own tracks, so reversing the index re-reads a timeline
        /// that is a function of the frame rather than a sequence.
        ///
        /// <para>P29's fit error is gone: content past the owning clip's
        /// <c>Duration</c> truncates silently, which is what the loop bound and
        /// the window exhaustion below already do.</para>
        /// </summary>
        private static void CompileChildClipTrack(
            NeoGeneratedClassValue target,
            string childClipKey,
            NeoAnimationSelector selector,
            int startFrame,
            NeoPlayDirection direction,
            int? offsetStart,
            int? offsetEnd,
            int parentFps,
            int parentDuration,
            Dictionary<int, Action[]> actionsByIndex,
            List<Action> prepareActions,
            List<IDisposable> disposables,
            string label,
            HashSet<string> compileStack,
            HashSet<string> activePlaybackStack)
        {
            var selectedDefinitions = new NeoSelectedChildClipDefinitions(
                target,
                childClipKey,
                label,
                compileStack,
                activePlaybackStack);
            disposables.Add(selectedDefinitions);
            prepareActions.Add(selectedDefinitions.PreparePlayback);
            if (selector.Refresh == NeoSelectorRefreshKind.OnLoad)
            {
                selectedDefinitions.GetOrCreate(selector.Resolve());
            }
            for (int parentFrame = startFrame; parentFrame < parentDuration; parentFrame++)
            {
                int capturedParentFrame = parentFrame;
                AddFrameAction(
                    actionsByIndex,
                    parentFrame,
                    () =>
                    {
                        NeoMemberClass placedChild = selector.Resolve();
                        NeoSelectedChildClip selected =
                            selectedDefinitions.GetOrCreate(placedChild);
                        NeoAnimationDefinition childDefinition =
                            selected.Definition;
                        if (!NeoAnimationPlayback.TryCropWindow(
                                childDefinition.Duration,
                                offsetStart,
                                offsetEnd,
                                out NeoAnimationCropWindow window))
                        {
                            return;
                        }
                        double rate = NeoAnimationPlayback.ChildClipContentFrameRate(
                            childDefinition.FPS,
                            parentFps,
                            label);
                        int childFrame = NeoAnimationPlayback.ContentIndexAtClipFrame(
                            parentDuration,
                            capturedParentFrame,
                            startFrame,
                            rate,
                            direction,
                            window);
                        if (childFrame == NeoAnimationPlayback.WritesNothing) return;
                        // Re-apply even when the child frame is unchanged.
                        // Earlier tracks may need to restore their selected
                        // child after a later PerFrame selector moved away.
                        childDefinition.ApplyFrame(
                            childFrame,
                            useResolvedState: true);
                    });
            }
        }

        private sealed class NeoSelectedChildClip
        {
            internal NeoSelectedChildClip(NeoAnimationDefinition definition)
            {
                Definition = definition;
            }

            internal NeoAnimationDefinition Definition { get; }
        }

        private sealed class NeoSelectedChildClipDefinitions : IDisposable
        {
            private readonly NeoGeneratedClassValue target;
            private readonly string childClipKey;
            private readonly string label;
            private readonly HashSet<string> compileStack;
            private readonly HashSet<string> activePlaybackStack;
            private readonly Dictionary<string, NeoSelectedChildClip> byChildId =
                new Dictionary<string, NeoSelectedChildClip>(StringComparer.Ordinal);
            private bool playbackPrepared;

            internal NeoSelectedChildClipDefinitions(
                NeoGeneratedClassValue target,
                string childClipKey,
                string label,
                HashSet<string> compileStack,
                HashSet<string> activePlaybackStack)
            {
                this.target = target;
                this.childClipKey = childClipKey;
                this.label = label;
                this.compileStack = compileStack;
                this.activePlaybackStack = activePlaybackStack;
            }

            internal NeoSelectedChildClip GetOrCreate(NeoMemberClass placedChild)
            {
                string childValueId = placedChild.value?.id
                    ?? throw new InvalidOperationException(
                        $"{label} selector resolved an unmaterialized child row.");
                if (byChildId.TryGetValue(childValueId, out NeoSelectedChildClip existing))
                {
                    return existing;
                }
                NeoGeneratedClassValue? childTarget =
                    target.Client.ResolveRegisteredGeneratedClassValue(childValueId);
                if (childTarget is null)
                {
                    throw new InvalidOperationException(
                        $"{label} ClipKey '{childClipKey}' cannot create a generated wrapper for selected child '{childValueId}'. Regenerate the project's C# types.");
                }
                NeoAnimationDefinition definition = Compile(
                    childTarget,
                    childClipKey,
                    compileStack,
                    activePlaybackStack);
                var created = new NeoSelectedChildClip(definition);
                byChildId[childValueId] = created;
                if (playbackPrepared) definition.PreparePlayback();
                return created;
            }

            internal void PreparePlayback()
            {
                playbackPrepared = true;
                foreach (NeoSelectedChildClip selected in byChildId.Values)
                {
                    selected.Definition.PreparePlayback();
                }
            }

            public void Dispose()
            {
                foreach (NeoSelectedChildClip selected in byChildId.Values)
                {
                    selected.Definition.Dispose();
                }
                byChildId.Clear();
            }
        }

        /// <summary>
        /// P48 §2.2's segment track: a leaf that writes one member of one child
        /// directly off the owning clip's clock, one content frame per clip
        /// frame.
        ///
        /// <para>Everything about the <b>content</b> is deferred to apply time
        /// (§3.1) — <c>BaseDuration</c> is the resolved segment's
        /// <c>Duration</c>, which a lookup-backed segment changes on equip — so
        /// unlike the child clip track above, the crop window cannot be
        /// computed here. What <i>is</i> compile-time is the write itself: the
        /// child's writable node and the target schema key are resolved once,
        /// exactly as <see cref="NeoAnimationCompiledWrite"/> does, so the
        /// per-frame cost is one member read and one SetValue.</para>
        /// </summary>
        private static void CompileSegmentTrack(
            NeoClient client,
            NeoMemberClass track,
            NeoAnimationSelector selector,
            int startFrame,
            NeoPlayDirection direction,
            int? offsetStart,
            int? offsetEnd,
            int parentDuration,
            Dictionary<int, Action[]> actionsByIndex,
            List<IDisposable> disposables,
            string label)
        {
            var source = new NeoAnimationSegmentSource(
                client,
                track,
                SegmentSchemaKey,
                label);
            disposables.Add(source);
            if (selector.Refresh == NeoSelectorRefreshKind.OnLoad)
            {
                selector.Resolve();
            }

            for (int parentFrame = startFrame; parentFrame < parentDuration; parentFrame++)
            {
                int capturedFrame = parentFrame;
                AddFrameAction(
                    actionsByIndex,
                    parentFrame,
                    () =>
                    {
                        NeoMemberClass placedChild = selector.Resolve();
                        NeoMemberClassWritable childWritable =
                            placedChild.AsWritableView();
                        if (childWritable.value is null) return;
                        string targetSchemaKey = ResolveSegmentTrackTargetKey(
                            client,
                            track,
                            placedChild,
                            label);
                        if (!NeoAnimationPlayback.TryCropWindow(
                                source.BaseDuration,
                                offsetStart,
                                offsetEnd,
                                out NeoAnimationCropWindow window))
                        {
                            return;
                        }
                        int index = NeoAnimationPlayback.ContentIndexAtClipFrame(
                            parentDuration,
                            capturedFrame,
                            startFrame,
                            contentFramesPerClipFrame: 1d,
                            direction,
                            window);
                        if (index == NeoAnimationPlayback.WritesNothing) return;
                        if (!source.TryReadContent(index, out MemberValue? row)) return;
                        // A frame that authored an Index but bound no Value row
                        // has nothing to say, which is §3.2's "writes nothing"
                        // reached one more way. An EXPLICIT null value is a
                        // different row and still writes — P42 §6's null leaf.
                        if (row is null) return;
                        NeoGeneratedTypesSupport.SetValue(
                            childWritable,
                            targetSchemaKey,
                            Payload(row));
                    });
            }
        }

        /// <summary>
        /// The schema key on the played child that this track's class names
        /// with <c>@settings(target:)</c>. Resolved through the class's
        /// <c>extendsClassId</c> chain (a project's own subclass inherits the
        /// target rather than restating it) and matched against the child's
        /// merged schema through each entry's <c>extendsMemberId</c> chain, so
        /// a child that overrides the targeted member still resolves.
        /// </summary>
        private static string ResolveSegmentTrackTargetKey(
            NeoClient client,
            NeoMemberClass track,
            NeoMemberClass placedChild,
            string label)
        {
            string? targetMemberId = ResolveTargetMemberId(client, TrackClassId(track));
            if (targetMemberId is null)
            {
                throw new InvalidOperationException(
                    $"{label} class '{TrackClassName(client, track)}' declares no target member, so it has nothing to write.");
            }
            string? childClassId = placedChild.value?.classId;
            if (string.IsNullOrWhiteSpace(childClassId))
            {
                throw new InvalidOperationException(
                    $"{label} plays against a child row with no class, so its target member cannot be resolved.");
            }
            foreach (MergedSchemaEntry entry in
                client.ResolveInstanceSurfaceSchema(childClassId!))
            {
                if (MemberDescendsFrom(client, entry.memberId, targetMemberId))
                {
                    return entry.schemaKey;
                }
            }
            throw new InvalidOperationException(
                $"{label} targets member '{targetMemberId}', which the played child's class '{childClassId}' does not declare.");
        }

        private static NeoMemberDelegate ValidateSelector(
            NeoMemberClass node,
            string label)
        {
            if (!node.TryGet("Selector", out NeoMemberDelegate? selector)
                || (selector.value?.value is null
                    && selector.member.defaultValue?.value is null))
            {
                throw new InvalidOperationException(
                    $"{label} must carry a valid NeoDelegate Selector.");
            }
            return selector;
        }

        private static NeoSelectorRefreshKind ReadSelectorRefresh(
            NeoMemberClass node,
            string label)
        {
            if (!node.TryGet("Refresh", out NeoMemberEnum? refresh))
            {
                return NeoSelectorRefreshKind.OnLoad;
            }
            string[] selected = refresh.Selected();
            if (selected.Length == 0) return NeoSelectorRefreshKind.OnLoad;
            if (selected.Length != 1
                || !NeoSelectorRefreshKind.IsKnown(selected[0]))
            {
                throw new InvalidOperationException(
                    $"{label} Refresh must be exactly one NeoSelectorRefreshKind option.");
            }
            return NeoSelectorRefreshKind.FromOptionId(selected[0]);
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
                    native.Dispatch == NeoFunctionDispatchKind.Asynchronous,
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
                    "Animation '~partial' structured-leaf rows are path segments into the leaf's fields, not leaf payloads: they are composed against the leaf's current value at apply time."),
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

        /// <summary>
        /// The concrete kind of one <c>Tracks</c> row. Dispatching on the row
        /// rather than on the list is the whole point of P48 §2.1's base class;
        /// a row that is neither shipped kind is named rather than reported as
        /// "not a child track", which was true of every segment track too.
        /// </summary>
        private enum NeoAnimationTrackKind
        {
            Unknown = 0,
            ChildClip,
            Segment,
        }

        private static NeoAnimationTrackKind ResolveTrackKind(NeoMemberClass track)
        {
            foreach (NeoSchemaClass schemaClass in track.inheritanceChain)
            {
                string? worldKind = schemaClass.system?["worldKind"]?.ToString();
                if (string.Equals(
                        worldKind,
                        AnimationChildTrackWorldKind,
                        StringComparison.Ordinal))
                {
                    return NeoAnimationTrackKind.ChildClip;
                }
                if (string.Equals(
                        worldKind,
                        AnimationSegmentTrackWorldKind,
                        StringComparison.Ordinal))
                {
                    return NeoAnimationTrackKind.Segment;
                }
            }
            return NeoAnimationTrackKind.Unknown;
        }

        private static string TrackClassId(NeoMemberClass track)
        {
            if (track.inheritanceChain.Count > 0) return track.inheritanceChain[0].id;
            return track.value?.classId ?? track.member.classId;
        }

        private static string TrackClassName(NeoClient client, NeoMemberClass track)
        {
            string classId = TrackClassId(track);
            return client.TryGetClass(classId, out NeoSchemaClass? schemaClass)
                ? schemaClass.name
                : classId;
        }

        /// <summary>
        /// P48 §2.1's <c>Direction</c>. An unset selection reads as
        /// <see cref="NeoPlayDirection.Forward"/>, matching the member's
        /// authored default; anything else than exactly one known option id is
        /// bad data and says so.
        /// </summary>
        private static NeoPlayDirection ReadTrackDirection(
            NeoMemberClass track,
            string label)
        {
            if (!track.TryGet("Direction", out NeoMemberEnum? direction))
            {
                return NeoPlayDirection.Forward;
            }
            string[] selected = direction.Selected();
            if (selected.Length == 0) return NeoPlayDirection.Forward;
            if (selected.Length != 1 || !NeoPlayDirection.IsKnown(selected[0]))
            {
                throw new InvalidOperationException(
                    $"{label} Direction must be exactly one NeoPlayDirection option.");
            }
            return NeoPlayDirection.FromOptionId(selected[0]);
        }

        /// <summary>
        /// P48 §2.1's crop window, in content frames. Both offsets are
        /// optional: a null start means "from the content's start" and a null
        /// end means "to the content's end", which is the reading that keeps
        /// playing the whole thing when a longer cosmetic is equipped.
        /// </summary>
        private static (int? start, int? end) ReadTrackCropWindow(
            NeoMemberClass track,
            string label)
        {
            int? start = ReadOptionalInt(track, "OffsetStartIndex", label);
            int? end = ReadOptionalInt(track, "OffsetEndIndex", label);
            if (start.HasValue && start.Value < 0)
            {
                throw new InvalidOperationException(
                    $"{label} OffsetStartIndex {start.Value} is negative.");
            }
            if (end.HasValue && end.Value < 1)
            {
                throw new InvalidOperationException(
                    $"{label} OffsetEndIndex {end.Value} must be at least 1; a window has to contain a frame.");
            }
            if (start.HasValue && end.HasValue && end.Value <= start.Value)
            {
                throw new InvalidOperationException(
                    $"{label} crop window [{start.Value}, {end.Value}) is empty or inverted, so the row can never play.");
            }
            return (start, end);
        }

        private static int? ReadOptionalInt(
            NeoMemberClass node,
            string key,
            string label)
        {
            if (!node.TryGet(key, out NeoMemberInt? value)) return null;
            if (value.value?.value is not double raw) return null;
            if (raw != Math.Truncate(raw))
            {
                throw new InvalidOperationException(
                    $"{label} Int member '{key}' must be an integer; found {raw}.");
            }
            return checked((int)raw);
        }

        /// <summary>
        /// The <c>@settings(target:)</c> member a track class or one of its
        /// bases names (P48 §2.2). Class-level metadata, so it resolves through
        /// <c>extendsClassId</c> — a project's own subclass of
        /// <c>NeoSpriteAnimationSegmentTrack</c> inherits the target.
        /// </summary>
        private static string? ResolveTargetMemberId(NeoClient client, string classId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = classId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (!client.TryGetClass(cursor!, out NeoSchemaClass? schemaClass)) return null;
                if (!string.IsNullOrWhiteSpace(schemaClass.targetMemberId))
                {
                    return schemaClass.targetMemberId;
                }
                cursor = schemaClass.extendsClassId;
            }
            return null;
        }

        private static bool MemberDescendsFrom(
            NeoClient client,
            string memberId,
            string ancestorMemberId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = memberId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (string.Equals(cursor, ancestorMemberId, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!client.TryGetMember(cursor!, out Member? member)) return false;
                cursor = member.extendsMemberId;
            }
            return false;
        }

        /// <summary>
        /// Whether a class or any of its ancestors declares
        /// <paramref name="worldKind"/> as its own world kind. Different
        /// question from <see cref="ResolveWorldKind"/>, which answers "the
        /// nearest world kind": a project subclass of
        /// <c>NeoSpriteAnimationSegmentTrack</c> resolves
        /// <c>spriteAnimationSegmentTrack</c> and still <b>is</b> a segment
        /// track.
        /// </summary>
        private static bool ClassInheritsWorldKind(
            NeoClient client,
            string? classId,
            string worldKind)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = classId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (!client.TryGetClass(cursor!, out NeoSchemaClass? schemaClass)) return false;
                if (string.Equals(
                        schemaClass.system?["worldKind"]?.ToString(),
                        worldKind,
                        StringComparison.Ordinal))
                {
                    return true;
                }
                cursor = schemaClass.extendsClassId;
            }
            return false;
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

    }
}
