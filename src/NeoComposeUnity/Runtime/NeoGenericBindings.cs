// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NeoCompose.Runtime.Json;
using UnityEngine;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Read/write codec for one generic-typed member
    /// (specs/class-generics.md Decision 12 / §9). Resolved lazily
    /// per instance from the node's substituted member record via
    /// <see cref="NeoGenericBindings.Resolve{T}(NeoClient, NeoMember)"/>
    /// and cached in a private field by generated code — no constructor
    /// threading.
    /// </summary>
    public sealed class NeoGenericBinding<T>
    {
        private readonly Func<NeoMember, T> read;
        private readonly Action<NeoMember, T> write;
        private readonly Func<T, NeoValueWritePayload?> serialize;

        internal NeoGenericBinding(
            MemberKind kind,
            Func<NeoMember, T> read,
            Action<NeoMember, T> write,
            Func<T, NeoValueWritePayload?> serialize)
        {
            Kind = kind;
            this.read = read;
            this.write = write;
            this.serialize = serialize;
        }

        /// <summary>The substituted runtime member kind this codec projects.</summary>
        public MemberKind Kind { get; }

        /// <summary>Reads the member's current value off the child node.</summary>
        public T Read(NeoMember node) => read(node);

        /// <summary>
        /// Writes <paramref name="value"/> through the child node's writable
        /// surface. Throws when the node is a read-only kind, and for
        /// collection kinds (collections mutate through the wrapper
        /// <see cref="Read"/> returns, never by whole-value assignment).
        /// </summary>
        public void Write(NeoMember node, T value) => write(node, value);

        /// <summary>
        /// Serializes <paramref name="value"/> to the write payload the
        /// saved collection wrappers expect — the <c>serializeItem</c>
        /// delegate body for a <see cref="NeoList{T}"/> /
        /// <see cref="NeoDictionary{T}"/> whose entry type is generic
        /// (codegen resolves this codec from the STAMPED collection node,
        /// because a new entry has no child node to resolve against).
        /// </summary>
        public NeoValueWritePayload? Serialize(T value) => serialize(value);
    }

    /// <summary>
    /// Resolves <see cref="NeoGenericBinding{T}"/> codecs from substituted
    /// member records (specs/class-generics.md §9). This is the
    /// runtime half of the cross-repo contract documented above
    /// <c>resolveGenericScopeForEmission</c> in the web codegen
    /// (<c>generate-unity-classes.ts</c>) — the emitted call surface is:
    ///
    /// <code>
    /// private NeoGenericBinding&lt;T&gt;? _speedGenericBinding;
    /// public T Speed
    /// {
    ///     get
    ///     {
    ///         var child = node.Get&lt;NeoMember&gt;("Speed");
    ///         _speedGenericBinding ??= NeoGenericBindings.Resolve&lt;T&gt;(client, child);
    ///         return _speedGenericBinding.Read(child);
    ///     }
    ///     set
    ///     {
    ///         var child = writableNode.Get&lt;NeoMember&gt;("Speed");
    ///         _speedGenericBinding ??= NeoGenericBindings.Resolve&lt;T&gt;(client, child);
    ///         _speedGenericBinding.Write(child, value);
    ///     }
    /// }
    /// </code>
    ///
    /// <para>Saved collection wrappers with a generic ENTRY additionally
    /// resolve an entry codec from the stamped collection node for their
    /// serializer hook:
    /// <c>item =&gt; NeoGenericBindings.Resolve&lt;TEntry&gt;(client, collectionNode).Serialize(item)</c>
    /// — when the passed node is a collection node and <c>T</c> is not that
    /// collection's own wrapper projection, resolution targets the node's
    /// (stamp-substituted) entry member.</para>
    ///
    /// <para><b>Accepted projections per substituted runtime kind</b> (the
    /// same C# types <c>csharpValueType</c> emits; kinds marked (†) accept
    /// the nullable form too — a non-nullable value-type read throws when
    /// no value is stored):</para>
    /// <list type="bullet">
    ///   <item><description>Bool → <c>bool</c> (†)</description></item>
    ///   <item><description>Int → <c>int</c> (†)</description></item>
    ///   <item><description>Float → <c>double</c> (†)</description></item>
    ///   <item><description>Decimal → <c>decimal</c> (†)</description></item>
    ///   <item><description>String → <c>string</c> (localized strings read
    ///   resolved text)</description></item>
    ///   <item><description>Color → <c>NeoReadOnlyColor</c> /
    ///   <c>NeoColor</c> (bound wrappers; unset optional reads null), or the
    ///   native <c>Color</c> (†)</description></item>
    ///   <item><description>Vector2/2Int/3/3Int → the matching
    ///   <c>NeoReadOnlyVector*</c> / <c>NeoVector*</c> wrapper, or the
    ///   native <c>Vector*</c> (†)</description></item>
    ///   <item><description>Sprite → <c>Sprite</c>; Audio →
    ///   <c>AudioClip</c></description></item>
    ///   <item><description>Enum (single-select) → the generated enum
    ///   wrapper class (matched by its implicit string conversions — the
    ///   cross-repo wrapper contract) or raw <c>string</c> option
    ///   id</description></item>
    ///   <item><description>Enum (multiSelect) →
    ///   <c>IReadOnlyList&lt;Wrapper&gt;</c> or raw
    ///   <c>string[]</c></description></item>
    ///   <item><description>Class → the generated class; dispatched through
    ///   the class's generated <c>Create</c>/<c>CreateWritable</c> factory
    ///   (reflected once per <c>T</c>), matching the registry+checked-cast
    ///   semantics constructed-Class slots use</description></item>
    ///   <item><description>List → <c>NeoReadOnlyList&lt;TEntry&gt;</c> /
    ///   <c>NeoList&lt;TEntry&gt;</c>; Dictionary →
    ///   <c>NeoReadOnlyDictionary&lt;TEntry&gt;</c> /
    ///   <c>NeoDictionary&lt;TEntry&gt;</c>. Read-as-wrapper only —
    ///   <see cref="NeoGenericBinding{T}.Write"/> throws; mutate through the
    ///   wrapper. Under IL2CPP emit one
    ///   <see cref="AotSeedCollectionCodecs{TEntry}"/> call per closed
    ///   collection construction.</description></item>
    /// </list>
    ///
    /// <para>A <c>T</c>/runtime-kind mismatch throws a descriptive error
    /// naming the member, the substituted kind's expected projection,
    /// and the actual generic argument.</para>
    /// </summary>
    public static class NeoGenericBindings
    {
        /// <summary>
        /// Resolves the codec for <paramref name="node"/>. For scalar slots
        /// the node's own (already substituted) member record decides the
        /// kind; for a collection node whose own wrapper projection does not
        /// match <typeparamref name="T"/>, resolution targets the node's
        /// stamp-substituted ENTRY member (the saved-wrapper serializer
        /// hook shape).
        /// </summary>
        public static NeoGenericBinding<T> Resolve<T>(NeoClient client, NeoMember node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(
                    nameof(node),
                    "NeoGenericBindings.Resolve requires the member's child node.");
            }
            if (node is NeoMemberList listNode
                && !TargetsOwnCollectionWrapper<T>(client, listNode.EntryMember, isList: true))
            {
                return ResolveForMember<T>(client, listNode.EntryMember);
            }
            if (node is NeoMemberDictionary dictionaryNode
                && !TargetsOwnCollectionWrapper<T>(client, dictionaryNode.EntryMember, isList: false))
            {
                return ResolveForMember<T>(client, dictionaryNode.EntryMember);
            }
            return ResolveForMember<T>(client, node.member);
        }

        /// <summary>
        /// True when <typeparamref name="T"/> should be treated as the
        /// collection node's OWN wrapper projection rather than an entry
        /// projection: it is the matching wrapper shape AND the entry is not
        /// itself a collection (a nested-collection entry's projection is
        /// also a wrapper — the entry codec wins there, matching the
        /// serializer-hook emission).
        /// </summary>
        private static bool TargetsOwnCollectionWrapper<T>(
            NeoClient client,
            Member entryMember,
            bool isList)
        {
            Type target = typeof(T);
            if (!target.IsGenericType) return false;
            Type definition = target.GetGenericTypeDefinition();
            bool isOwnWrapperShape = isList
                ? definition == typeof(NeoReadOnlyList<>) || definition == typeof(NeoList<>)
                : definition == typeof(NeoReadOnlyDictionary<>)
                    || definition == typeof(NeoDictionary<>)
                    || definition == typeof(NeoReadOnlyDictionary<,>)
                    || definition == typeof(NeoDictionary<,>);
            if (!isOwnWrapperShape) return false;
            bool entryIsCollection = entryMember is ListMember
                || entryMember is DictionaryMember;
            return !entryIsCollection;
        }

        /// <summary>
        /// Record-based core — resolves a codec from a substituted member
        /// record directly (used for collection entry codecs, where the
        /// entry member is known before any entry node exists).
        /// </summary>
        internal static NeoGenericBinding<T> ResolveForMember<T>(
            NeoClient client,
            Member member)
        {
            switch (member.kind)
            {
                case MemberKind.Bool: return BoolCodec<T>(member);
                case MemberKind.Int: return IntCodec<T>(member);
                case MemberKind.Float: return FloatCodec<T>(member);
                case MemberKind.Decimal: return DecimalCodec<T>(member);
                case MemberKind.String: return StringCodec<T>(member);
                case MemberKind.Color: return ColorCodec<T>(member);
                case MemberKind.Vector2: return Vector2Codec<T>(member);
                case MemberKind.Vector2Int: return Vector2IntCodec<T>(member);
                case MemberKind.Vector3: return Vector3Codec<T>(member);
                case MemberKind.Vector3Int: return Vector3IntCodec<T>(member);
                case MemberKind.Sprite: return SpriteCodec<T>(client, member);
                case MemberKind.Audio: return AudioCodec<T>(client, member);
                case MemberKind.Enum: return EnumCodec<T>(member);
                case MemberKind.Class: return ClassCodec<T>(client, member);
                case MemberKind.List: return ListCodec<T>(client, member);
                case MemberKind.Dictionary: return DictionaryCodec<T>(client, member);
                case MemberKind.Generic:
                    throw new InvalidOperationException(
                        $"NeoGenericBindings.Resolve received the un-substituted Generic slot '{member.name}' ({member.id}) — substitution must run before codec resolution (is the node's parent a Class node with a closed class context?).");
                default:
                    throw new InvalidOperationException(
                        $"NeoGenericBindings.Resolve: runtime kind {member.kind} of member '{member.name}' ({member.id}) is not an eligible generic binding kind (spec Decision 14).");
            }
        }

        /// <summary>
        /// IL2CPP AOT seed — statically references the collection- and
        /// enum-list-codec instantiations for <typeparamref name="TEntry"/>
        /// without executing them. Generated code emits one call per closed
        /// generic collection construction (e.g.
        /// <c>NeoGenericBindings.AotSeedCollectionCodecs&lt;double&gt;()</c>
        /// when a type closes <c>T = List&lt;Float&gt;</c>) so IL2CPP
        /// compiles the value-type instantiations reflection alone would
        /// miss.
        /// </summary>
        public static void AotSeedCollectionCodecs<TEntry>()
        {
            if (!aotSeedTrap) return;
            // Never executed (aotSeedTrap is always false) — the calls only
            // exist so the AOT compiler sees the closed instantiations.
            CreateReadOnlyListCodec<TEntry>(null!, null!);
            CreateListCodec<TEntry>(null!, null!);
            CreateReadOnlyDictionaryCodec<TEntry>(null!, null!);
            CreateDictionaryCodec<TEntry>(null!, null!);
            CreateEnumListCodec<TEntry>(null!);
        }

        private static volatile bool aotSeedTrap;

        // ------------------------------------------------------------------
        // Shared helpers.
        // ------------------------------------------------------------------

        private static InvalidOperationException Mismatch<T>(
            Member member,
            string expectedProjection)
        {
            return new InvalidOperationException(
                $"Generic binding mismatch on member '{member.name}' ({member.id}): the substituted runtime kind {member.kind} projects '{expectedProjection}', but the generic argument is '{typeof(T).FullName}'.");
        }

        private static InvalidOperationException MissingValue<T>(Member member)
        {
            return new InvalidOperationException(
                $"Generic member '{member.name}' ({member.id}) has no stored value, but the generic argument '{typeof(T).FullName}' is non-nullable.");
        }

        private static TNode RequireNode<TNode>(NeoMember node, Member member)
            where TNode : NeoMember
        {
            if (node is TNode match) return match;
            throw new InvalidOperationException(
                $"Generic member '{member.name}' ({member.id}) resolved a {node.GetType().Name} node where a {typeof(TNode).Name} was expected — the node was constructed from a different substitution than this codec.");
        }

        private static TNode RequireWritable<TNode>(NeoMember node, Member member)
            where TNode : NeoMember
        {
            if (node is TNode match) return match;
            throw new InvalidOperationException(
                $"Cannot write generic member '{member.name}' ({member.id}): the node is the read-only {node.GetType().Name}; writes require the {typeof(TNode).Name} constructed under Save/Session ownership.");
        }

        private static NeoGenericBinding<T> Adapt<T, TActual>(NeoGenericBinding<TActual> codec)
        {
            // Callers verify typeof(T) == typeof(TActual) before adapting,
            // so the double cast is an identity conversion.
            return (NeoGenericBinding<T>)(object)codec;
        }

        // ------------------------------------------------------------------
        // Scalar codecs.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> BoolCodec<T>(Member member)
        {
            if (typeof(T) == typeof(bool?))
            {
                return Adapt<T, bool?>(new NeoGenericBinding<bool?>(
                    MemberKind.Bool,
                    node => RequireNode<NeoMemberBool>(node, member).value?.value,
                    (node, v) => RequireWritable<NeoMemberBoolWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            if (typeof(T) == typeof(bool))
            {
                return Adapt<T, bool>(new NeoGenericBinding<bool>(
                    MemberKind.Bool,
                    node => RequireNode<NeoMemberBool>(node, member).value?.value
                        ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberBoolWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            throw Mismatch<T>(member, "bool' or 'bool?");
        }

        private static NeoGenericBinding<T> IntCodec<T>(Member member)
        {
            if (typeof(T) == typeof(int?))
            {
                return Adapt<T, int?>(new NeoGenericBinding<int?>(
                    MemberKind.Int,
                    node => ReadInt(node, member),
                    (node, v) => RequireWritable<NeoMemberIntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(v.HasValue ? (double?)v.Value : null)));
            }
            if (typeof(T) == typeof(int))
            {
                return Adapt<T, int>(new NeoGenericBinding<int>(
                    MemberKind.Int,
                    node => ReadInt(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberIntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue((double)v)));
            }
            throw Mismatch<T>(member, "int' or 'int?");
        }

        private static int? ReadInt(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberInt>(node, member).value?.value;
            return raw.HasValue ? (int)raw.Value : null;
        }

        private static NeoGenericBinding<T> FloatCodec<T>(Member member)
        {
            if (typeof(T) == typeof(double?))
            {
                return Adapt<T, double?>(new NeoGenericBinding<double?>(
                    MemberKind.Float,
                    node => RequireNode<NeoMemberFloat>(node, member).value?.value,
                    (node, v) => RequireWritable<NeoMemberFloatWritable>(node, member)
                        .Set(v.HasValue ? (float?)v.Value : null),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            if (typeof(T) == typeof(double))
            {
                return Adapt<T, double>(new NeoGenericBinding<double>(
                    MemberKind.Float,
                    node => RequireNode<NeoMemberFloat>(node, member).value?.value
                        ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberFloatWritable>(node, member)
                        .Set((float)v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            throw Mismatch<T>(member, "double' or 'double?");
        }

        private static NeoGenericBinding<T> DecimalCodec<T>(Member member)
        {
            if (typeof(T) == typeof(decimal?))
            {
                return Adapt<T, decimal?>(new NeoGenericBinding<decimal?>(
                    MemberKind.Decimal,
                    node => NeoDecimalValues.ParseOrNull(
                        RequireNode<NeoMemberDecimal>(node, member).value?.value),
                    (node, v) => RequireWritable<NeoMemberDecimalWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoDecimalValues.FormatOrNull(v))));
            }
            if (typeof(T) == typeof(decimal))
            {
                return Adapt<T, decimal>(new NeoGenericBinding<decimal>(
                    MemberKind.Decimal,
                    node => NeoDecimalValues.ParseOrNull(
                        RequireNode<NeoMemberDecimal>(node, member).value?.value)
                        ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberDecimalWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoDecimalValues.Format(v))));
            }
            throw Mismatch<T>(member, "decimal' or 'decimal?");
        }

        private static NeoGenericBinding<T> StringCodec<T>(Member member)
        {
            if (typeof(T) != typeof(string))
            {
                throw Mismatch<T>(member, "string");
            }
            bool localized = member is StringMember stringMember
                && stringMember.EffectiveFormat == NeoStringFormatKind.Localized;
            return Adapt<T, string?>(new NeoGenericBinding<string?>(
                MemberKind.String,
                node =>
                {
                    var stringNode = RequireNode<NeoMemberString>(node, member);
                    string? text = localized ? stringNode.Text : stringNode.value?.value;
                    if (text is null && member.EffectiveRequirement == NeoMemberRequirementKind.Required)
                    {
                        throw new InvalidOperationException(
                            $"Required string '{member.name}' ({member.id}) has no value.");
                    }
                    return text;
                },
                (node, v) => RequireWritable<NeoMemberStringWritable>(node, member).Set(v),
                v => NeoValueWritePayload.FromValue(v)));
        }

        // ------------------------------------------------------------------
        // Color codec — codegen projects the bound wrapper family
        // (NeoReadOnlyColor read-only / NeoColor saved); the native Color
        // forms are additionally accepted for hand-written call sites.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> ColorCodec<T>(Member member)
        {
            if (typeof(T) == typeof(NeoColor))
            {
                return Adapt<T, NeoColor?>(new NeoGenericBinding<NeoColor?>(
                    MemberKind.Color,
                    node =>
                    {
                        var colorNode = RequireNode<NeoMemberColor>(node, member);
                        return colorNode.value?.value is null ? null : new NeoColor(colorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberColorWritable>(node, member)
                        .Set(v is null ? (Color?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.ColorValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyColor))
            {
                return Adapt<T, NeoReadOnlyColor?>(new NeoGenericBinding<NeoReadOnlyColor?>(
                    MemberKind.Color,
                    node =>
                    {
                        var colorNode = RequireNode<NeoMemberColor>(node, member);
                        return colorNode.value?.value is null ? null : new NeoReadOnlyColor(colorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberColorWritable>(node, member)
                        .Set(v is null ? (Color?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.ColorValue(v.Value))));
            }
            if (typeof(T) == typeof(Color?))
            {
                return Adapt<T, Color?>(new NeoGenericBinding<Color?>(
                    MemberKind.Color,
                    node => ReadColor(node, member),
                    (node, v) => RequireWritable<NeoMemberColorWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.ColorValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Color))
            {
                return Adapt<T, Color>(new NeoGenericBinding<Color>(
                    MemberKind.Color,
                    node => ReadColor(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberColorWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.ColorValue(v))));
            }
            throw Mismatch<T>(member, "NeoReadOnlyColor', 'NeoColor', 'Color' or 'Color?");
        }

        private static Color? ReadColor(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberColor>(node, member).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadColorValue(raw);
        }

        // ------------------------------------------------------------------
        // Vector codecs — same wrapper-family projection as Color.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> Vector2Codec<T>(Member member)
        {
            if (typeof(T) == typeof(NeoVector2))
            {
                return Adapt<T, NeoVector2?>(new NeoGenericBinding<NeoVector2?>(
                    MemberKind.Vector2,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector2>(node, member);
                        return vectorNode.value?.value is null ? null : new NeoVector2(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector2Writable>(node, member)
                        .Set(v is null ? (Vector2?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2Value(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector2))
            {
                return Adapt<T, NeoReadOnlyVector2?>(new NeoGenericBinding<NeoReadOnlyVector2?>(
                    MemberKind.Vector2,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector2>(node, member);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector2(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector2Writable>(node, member)
                        .Set(v is null ? (Vector2?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2Value(v.Value))));
            }
            if (typeof(T) == typeof(Vector2?))
            {
                return Adapt<T, Vector2?>(new NeoGenericBinding<Vector2?>(
                    MemberKind.Vector2,
                    node => ReadVector2(node, member),
                    (node, v) => RequireWritable<NeoMemberVector2Writable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector2Value(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector2))
            {
                return Adapt<T, Vector2>(new NeoGenericBinding<Vector2>(
                    MemberKind.Vector2,
                    node => ReadVector2(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberVector2Writable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector2Value(v))));
            }
            throw Mismatch<T>(
                member,
                "NeoReadOnlyVector2', 'NeoVector2', 'Vector2' or 'Vector2?");
        }

        private static Vector2? ReadVector2(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberVector2>(node, member).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector2Value(raw);
        }

        private static NeoGenericBinding<T> Vector2IntCodec<T>(Member member)
        {
            if (typeof(T) == typeof(NeoVector2Int))
            {
                return Adapt<T, NeoVector2Int?>(new NeoGenericBinding<NeoVector2Int?>(
                    MemberKind.Vector2Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector2Int>(node, member);
                        return vectorNode.value?.value is null ? null : new NeoVector2Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector2IntWritable>(node, member)
                        .Set(v is null ? (Vector2Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2IntValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector2Int))
            {
                return Adapt<T, NeoReadOnlyVector2Int?>(new NeoGenericBinding<NeoReadOnlyVector2Int?>(
                    MemberKind.Vector2Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector2Int>(node, member);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector2Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector2IntWritable>(node, member)
                        .Set(v is null ? (Vector2Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2IntValue(v.Value))));
            }
            if (typeof(T) == typeof(Vector2Int?))
            {
                return Adapt<T, Vector2Int?>(new NeoGenericBinding<Vector2Int?>(
                    MemberKind.Vector2Int,
                    node => ReadVector2Int(node, member),
                    (node, v) => RequireWritable<NeoMemberVector2IntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector2IntValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector2Int))
            {
                return Adapt<T, Vector2Int>(new NeoGenericBinding<Vector2Int>(
                    MemberKind.Vector2Int,
                    node => ReadVector2Int(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberVector2IntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector2IntValue(v))));
            }
            throw Mismatch<T>(
                member,
                "NeoReadOnlyVector2Int', 'NeoVector2Int', 'Vector2Int' or 'Vector2Int?");
        }

        private static Vector2Int? ReadVector2Int(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberVector2Int>(node, member).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector2IntValue(raw);
        }

        private static NeoGenericBinding<T> Vector3Codec<T>(Member member)
        {
            if (typeof(T) == typeof(NeoVector3))
            {
                return Adapt<T, NeoVector3?>(new NeoGenericBinding<NeoVector3?>(
                    MemberKind.Vector3,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector3>(node, member);
                        return vectorNode.value?.value is null ? null : new NeoVector3(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector3Writable>(node, member)
                        .Set(v is null ? (Vector3?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3Value(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector3))
            {
                return Adapt<T, NeoReadOnlyVector3?>(new NeoGenericBinding<NeoReadOnlyVector3?>(
                    MemberKind.Vector3,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector3>(node, member);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector3(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector3Writable>(node, member)
                        .Set(v is null ? (Vector3?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3Value(v.Value))));
            }
            if (typeof(T) == typeof(Vector3?))
            {
                return Adapt<T, Vector3?>(new NeoGenericBinding<Vector3?>(
                    MemberKind.Vector3,
                    node => ReadVector3(node, member),
                    (node, v) => RequireWritable<NeoMemberVector3Writable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector3Value(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector3))
            {
                return Adapt<T, Vector3>(new NeoGenericBinding<Vector3>(
                    MemberKind.Vector3,
                    node => ReadVector3(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberVector3Writable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector3Value(v))));
            }
            throw Mismatch<T>(
                member,
                "NeoReadOnlyVector3', 'NeoVector3', 'Vector3' or 'Vector3?");
        }

        private static Vector3? ReadVector3(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberVector3>(node, member).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector3Value(raw);
        }

        private static NeoGenericBinding<T> Vector3IntCodec<T>(Member member)
        {
            if (typeof(T) == typeof(NeoVector3Int))
            {
                return Adapt<T, NeoVector3Int?>(new NeoGenericBinding<NeoVector3Int?>(
                    MemberKind.Vector3Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector3Int>(node, member);
                        return vectorNode.value?.value is null ? null : new NeoVector3Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector3IntWritable>(node, member)
                        .Set(v is null ? (Vector3Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3IntValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector3Int))
            {
                return Adapt<T, NeoReadOnlyVector3Int?>(new NeoGenericBinding<NeoReadOnlyVector3Int?>(
                    MemberKind.Vector3Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoMemberVector3Int>(node, member);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector3Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoMemberVector3IntWritable>(node, member)
                        .Set(v is null ? (Vector3Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3IntValue(v.Value))));
            }
            if (typeof(T) == typeof(Vector3Int?))
            {
                return Adapt<T, Vector3Int?>(new NeoGenericBinding<Vector3Int?>(
                    MemberKind.Vector3Int,
                    node => ReadVector3Int(node, member),
                    (node, v) => RequireWritable<NeoMemberVector3IntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector3IntValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector3Int))
            {
                return Adapt<T, Vector3Int>(new NeoGenericBinding<Vector3Int>(
                    MemberKind.Vector3Int,
                    node => ReadVector3Int(node, member) ?? throw MissingValue<T>(member),
                    (node, v) => RequireWritable<NeoMemberVector3IntWritable>(node, member).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector3IntValue(v))));
            }
            throw Mismatch<T>(
                member,
                "NeoReadOnlyVector3Int', 'NeoVector3Int', 'Vector3Int' or 'Vector3Int?");
        }

        private static Vector3Int? ReadVector3Int(NeoMember node, Member member)
        {
            var raw = RequireNode<NeoMemberVector3Int>(node, member).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector3IntValue(raw);
        }

        // ------------------------------------------------------------------
        // File codecs.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> SpriteCodec<T>(NeoClient client, Member member)
        {
            if (typeof(T) != typeof(Sprite))
            {
                throw Mismatch<T>(member, "UnityEngine.Sprite");
            }
            return Adapt<T, Sprite?>(new NeoGenericBinding<Sprite?>(
                MemberKind.Sprite,
                node => RequireNode<NeoMemberSprite>(node, member).Resolve(),
                (node, v) => RequireWritable<NeoMemberSpriteWritable>(node, member).Set(v),
                v => NeoValueWritePayload.FromValue(
                    NeoGeneratedTypesSupport.SpriteValue(client, v, null, member.name))));
        }

        private static NeoGenericBinding<T> AudioCodec<T>(NeoClient client, Member member)
        {
            if (typeof(T) != typeof(AudioClip))
            {
                throw Mismatch<T>(member, "UnityEngine.AudioClip");
            }
            return Adapt<T, AudioClip?>(new NeoGenericBinding<AudioClip?>(
                MemberKind.Audio,
                node =>
                {
                    var resolved = RequireNode<NeoMemberAudio>(node, member).Resolve();
                    if (resolved is null && member.EffectiveRequirement == NeoMemberRequirementKind.Required)
                    {
                        throw new InvalidOperationException(
                            $"Required Audio '{member.name}' ({member.id}) has no synchronized asset.");
                    }
                    return resolved;
                },
                (node, v) => RequireWritable<NeoMemberAudioWritable>(node, member).Set(v),
                v => NeoValueWritePayload.FromValue(
                    NeoGeneratedTypesSupport.AudioValue(client, v, null, member.name))));
        }

        // ------------------------------------------------------------------
        // Enum codec — the generated wrapper contract is the pair of
        // implicit string conversions every emitted enum wrapper carries.
        // ------------------------------------------------------------------

        private static class EnumWrapperOps<T>
        {
            public static readonly Func<string, T>? FromOptionId;
            public static readonly Func<T, string>? ToOptionId;

            static EnumWrapperOps()
            {
                foreach (var method in typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != "op_Implicit") continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1) continue;
                    if (method.ReturnType == typeof(T)
                        && parameters[0].ParameterType == typeof(string))
                    {
                        FromOptionId = (Func<string, T>)method.CreateDelegate(typeof(Func<string, T>));
                    }
                    if (method.ReturnType == typeof(string)
                        && parameters[0].ParameterType == typeof(T))
                    {
                        ToOptionId = (Func<T, string>)method.CreateDelegate(typeof(Func<T, string>));
                    }
                }
            }
        }

        private static NeoGenericBinding<T> EnumCodec<T>(Member member)
        {
            bool multiSelect = member is EnumMember enumMember && enumMember.EffectiveSelection == NeoMemberSelectionKind.Multi;
            if (multiSelect)
            {
                if (typeof(T) == typeof(string[]))
                {
                    return Adapt<T, string[]>(new NeoGenericBinding<string[]>(
                        MemberKind.Enum,
                        node => RequireNode<NeoMemberEnum>(node, member).Selected(),
                        (node, v) => RequireWritable<NeoMemberEnumWritable>(node, member).Set(v),
                        v => NeoValueWritePayload.FromValue(v)));
                }
                if (typeof(T).IsGenericType
                    && typeof(T).GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    return InvokeGenericCore<T>(
                        member,
                        nameof(CreateEnumListCodec),
                        typeof(T).GetGenericArguments()[0],
                        new object?[] { member });
                }
                throw Mismatch<T>(
                    member,
                    "IReadOnlyList<Wrapper>' (generated enum wrappers) or 'string[]' (option ids)");
            }
            if (typeof(T) == typeof(string))
            {
                return Adapt<T, string?>(new NeoGenericBinding<string?>(
                    MemberKind.Enum,
                    node => NeoGeneratedTypesSupport.ReadSingleSelected(
                        RequireNode<NeoMemberEnum>(node, member)),
                    (node, v) => RequireWritable<NeoMemberEnumWritable>(node, member)
                        .Set(v is null ? null : new[] { v }),
                    v => NeoValueWritePayload.FromValue(v is null ? null : new[] { v })));
            }
            var fromOptionId = EnumWrapperOps<T>.FromOptionId;
            if (fromOptionId is null)
            {
                throw Mismatch<T>(
                    member,
                    "a generated enum wrapper with an implicit string→wrapper conversion, or 'string");
            }
            var toOptionId = EnumWrapperOps<T>.ToOptionId;
            if (toOptionId is null)
            {
                throw Mismatch<T>(
                    member,
                    "a generated enum wrapper with an implicit wrapper→string conversion, or 'string");
            }
            return new NeoGenericBinding<T>(
                MemberKind.Enum,
                node =>
                {
                    string? selected = NeoGeneratedTypesSupport.ReadSingleSelected(
                        RequireNode<NeoMemberEnum>(node, member));
                    return selected is null ? default! : fromOptionId(selected);
                },
                (node, v) => RequireWritable<NeoMemberEnumWritable>(node, member)
                    .Set(v is null ? null : new[] { toOptionId(v) }),
                v => NeoValueWritePayload.FromValue(v is null ? null : new[] { toOptionId(v) }));
        }

        private static NeoGenericBinding<IReadOnlyList<TWrapper>> CreateEnumListCodec<TWrapper>(
            Member member)
        {
            var fromOptionId = EnumWrapperOps<TWrapper>.FromOptionId;
            if (fromOptionId is null)
            {
                throw Mismatch<IReadOnlyList<TWrapper>>(
                    member,
                    "IReadOnlyList<Wrapper>' where Wrapper carries an implicit string→wrapper conversion");
            }
            var toOptionId = EnumWrapperOps<TWrapper>.ToOptionId;
            if (toOptionId is null)
            {
                throw Mismatch<IReadOnlyList<TWrapper>>(
                    member,
                    "IReadOnlyList<Wrapper>' where Wrapper carries an implicit wrapper→string conversion");
            }
            string[] ToOptionIds(IReadOnlyList<TWrapper>? wrappers)
            {
                if (wrappers is null) return Array.Empty<string>();
                var ids = new string[wrappers.Count];
                for (int i = 0; i < wrappers.Count; i++) ids[i] = toOptionId(wrappers[i]);
                return ids;
            }
            return new NeoGenericBinding<IReadOnlyList<TWrapper>>(
                MemberKind.Enum,
                node => NeoGeneratedTypesSupport.ReadEnumList(
                    RequireNode<NeoMemberEnum>(node, member).Selected(),
                    fromOptionId),
                (node, v) => RequireWritable<NeoMemberEnumWritable>(node, member)
                    .Set(v is null ? null : ToOptionIds(v)),
                v => NeoValueWritePayload.FromValue(v is null ? null : ToOptionIds(v)));
        }

        // ------------------------------------------------------------------
        // Class codec — dispatches through the generated per-class
        // Create/CreateWritable factories (reflected once per T), the same
        // registry+checked-cast semantics constructed-Class slots use
        // (spec Decision 12: instances stay nominal).
        // ------------------------------------------------------------------

        private static class GeneratedFactoryOps<T>
        {
            public static readonly MethodInfo? Create;
            public static readonly MethodInfo? CreateWritable;

            static GeneratedFactoryOps()
            {
                const BindingFlags flags =
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                Create = FindFactory("Create", typeof(NeoMemberClass), flags);
                CreateWritable = FindFactory(
                    "CreateWritable", typeof(NeoMemberClassWritable), flags);
            }

            private static MethodInfo? FindFactory(string name, Type nodeType, BindingFlags flags)
            {
                foreach (var method in typeof(T).GetMethods(flags))
                {
                    if (method.Name != name) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 2) continue;
                    if (parameters[0].ParameterType != typeof(NeoClient)) continue;
                    if (parameters[1].ParameterType != nodeType) continue;
                    if (!typeof(T).IsAssignableFrom(method.ReturnType)) continue;
                    return method;
                }
                return null;
            }
        }

        private static NeoGenericBinding<T> ClassCodec<T>(NeoClient client, Member member)
        {
            if (member is not ClassMember classMember)
            {
                throw new InvalidOperationException(
                    $"NeoGenericBindings: member '{member.name}' ({member.id}) reports kind Class but is a {member.GetType().Name} record — the export is corrupt.");
            }
            if (GeneratedFactoryOps<T>.Create is null
                && GeneratedFactoryOps<T>.CreateWritable is null)
            {
                throw Mismatch<T>(
                    member,
                    $"a generated class for class '{classMember.classId}' exposing the generated Create/CreateWritable factory");
            }
            return new NeoGenericBinding<T>(
                MemberKind.Class,
                node =>
                {
                    var classNode = RequireNode<NeoMemberClass>(node, member);
                    if (classNode.value is null)
                    {
                        if (member.EffectiveRequirement == NeoMemberRequirementKind.Required)
                        {
                            throw new InvalidOperationException(
                                $"Required class member '{member.name}' ({member.id}) has no value.");
                        }
                        return default!;
                    }
                    object? resolved;
                    if (classNode is NeoMemberClassWritable writableNode
                        && GeneratedFactoryOps<T>.CreateWritable is not null)
                    {
                        resolved = GeneratedFactoryOps<T>.CreateWritable
                            .Invoke(null, new object[] { client, writableNode });
                    }
                    else if (GeneratedFactoryOps<T>.Create is not null)
                    {
                        resolved = GeneratedFactoryOps<T>.Create
                            .Invoke(null, new object[] { client, classNode });
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Class generic member '{member.name}' ({member.id}) resolved a read-only node, but '{typeof(T).FullName}' only exposes CreateWritable.");
                    }
                    if (resolved is not T typed)
                    {
                        throw new InvalidOperationException(
                            $"Class generic member '{member.name}' ({member.id}) resolved a '{resolved?.GetType().FullName ?? "null"}', which is not the generic argument '{typeof(T).FullName}' — the stored value's classId does not match the closed construction (authoring-time signature validation should make this unreachable).");
                    }
                    return typed;
                },
                (node, v) =>
                {
                    if (node.parent is not NeoMemberClassWritable parentRecord)
                    {
                        throw new InvalidOperationException(
                            $"Cannot assign class generic member '{member.name}' ({member.id}): the node has no writable class parent to rebind through.");
                    }
                    if (!parentRecord.TryGetSchemaKeyForChild(node, out string? schemaKey))
                    {
                        throw new InvalidOperationException(
                            $"Cannot assign class generic member '{member.name}' ({member.id}): the node is not a registered schema field of its parent.");
                    }
                    parentRecord.SetSerializedValue(schemaKey, SerializeClass(member, v));
                },
                v => SerializeClass(member, v));
        }

        private static NeoValueWritePayload? SerializeClass<T>(Member member, T value)
        {
            if (value is null) return null;
            if (value is INeoValueReference reference)
            {
                return NeoGeneratedTypesSupport.ValueReference(reference);
            }
            throw new InvalidOperationException(
                $"Cannot serialize '{value.GetType().FullName}' for class generic member '{member.name}' ({member.id}): the value does not expose a backing value id (INeoValueReference).");
        }

        // ------------------------------------------------------------------
        // Collection codecs — T is a closed NeoReadOnlyList<>/NeoList<>/
        // NeoReadOnlyDictionary<>/NeoDictionary<> construction; the entry
        // codec resolves recursively from the collection node's substituted
        // entry member.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> ListCodec<T>(NeoClient client, Member member)
        {
            Type target = typeof(T);
            if (!target.IsGenericType)
            {
                throw Mismatch<T>(member, "NeoReadOnlyList<TEntry>' or 'NeoList<TEntry>");
            }
            Type definition = target.GetGenericTypeDefinition();
            string coreName;
            if (definition == typeof(NeoReadOnlyList<>))
            {
                coreName = nameof(CreateReadOnlyListCodec);
            }
            else if (definition == typeof(NeoList<>))
            {
                coreName = nameof(CreateListCodec);
            }
            else
            {
                throw Mismatch<T>(member, "NeoReadOnlyList<TEntry>' or 'NeoList<TEntry>");
            }
            return InvokeGenericCore<T>(
                member,
                coreName,
                target.GetGenericArguments()[0],
                new object?[] { client, member });
        }

        private static NeoGenericBinding<T> DictionaryCodec<T>(NeoClient client, Member member)
        {
            Type target = typeof(T);
            if (!target.IsGenericType)
            {
                throw Mismatch<T>(member, "NeoReadOnlyDictionary<TEntry>' or 'NeoDictionary<TEntry>");
            }
            Type definition = target.GetGenericTypeDefinition();
            string coreName;
            if (definition == typeof(NeoReadOnlyDictionary<>))
            {
                coreName = nameof(CreateReadOnlyDictionaryCodec);
            }
            else if (definition == typeof(NeoDictionary<>))
            {
                coreName = nameof(CreateDictionaryCodec);
            }
            else if (definition == typeof(NeoReadOnlyDictionary<,>))
            {
                throw new InvalidOperationException(
                    $"Generic binding mismatch on member '{member.name}' ({member.id}): enum-keyed (two-arity) dictionary wrappers are not supported as generic bindings yet — use the single-arity NeoReadOnlyDictionary<TEntry> view.");
            }
            else if (definition == typeof(NeoDictionary<,>))
            {
                throw new InvalidOperationException(
                    $"Generic binding mismatch on member '{member.name}' ({member.id}): enum-keyed (two-arity) dictionary wrappers are not supported as generic bindings yet — use the single-arity NeoDictionary<TEntry> view.");
            }
            else
            {
                throw Mismatch<T>(member, "NeoReadOnlyDictionary<TEntry>' or 'NeoDictionary<TEntry>");
            }
            return InvokeGenericCore<T>(
                member,
                coreName,
                target.GetGenericArguments()[0],
                new object?[] { client, member });
        }

        private static NeoGenericBinding<T> InvokeGenericCore<T>(
            Member member,
            string coreName,
            Type typeArgument,
            object?[] arguments)
        {
            MethodInfo? core = typeof(NeoGenericBindings).GetMethod(
                coreName,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (core is null)
            {
                throw new InvalidOperationException(
                    $"NeoGenericBindings is missing its codec core '{coreName}' — this is an SDK bug (member '{member.id}').");
            }
            object? codec = core.MakeGenericMethod(typeArgument).Invoke(null, arguments);
            return (NeoGenericBinding<T>)codec!;
        }

        private static NeoGenericBinding<NeoReadOnlyList<TEntry>> CreateReadOnlyListCodec<TEntry>(
            NeoClient client,
            Member member)
        {
            return new NeoGenericBinding<NeoReadOnlyList<TEntry>>(
                MemberKind.List,
                node =>
                {
                    var listNode = RequireNode<NeoMemberList>(node, member);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    return new NeoReadOnlyList<TEntry>(
                        client,
                        listNode,
                        (c, child) =>
                        {
                            entryCodec ??= ResolveForMember<TEntry>(c, listNode.EntryMember);
                            return entryCodec.Read(child);
                        });
                },
                (node, v) => throw CollectionAssignment(member),
                v => throw CollectionSerialize(member));
        }

        private static NeoGenericBinding<NeoList<TEntry>> CreateListCodec<TEntry>(
            NeoClient client,
            Member member)
        {
            return new NeoGenericBinding<NeoList<TEntry>>(
                MemberKind.List,
                node =>
                {
                    var listNode = RequireNode<NeoMemberList>(node, member);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    NeoGenericBinding<TEntry> EntryCodec()
                    {
                        entryCodec ??= ResolveForMember<TEntry>(client, listNode.EntryMember);
                        return entryCodec;
                    }
                    return new NeoList<TEntry>(
                        client,
                        listNode,
                        () => listNode as NeoMemberListWritable
                            ?? throw new InvalidOperationException(
                                $"Cannot mutate list generic member '{member.name}' ({member.id}): the node is the read-only NeoMemberList; writes require Save/Session ownership."),
                        (c, child) => EntryCodec().Read(child),
                        item => EntryCodec().Serialize(item));
                },
                (node, v) => throw CollectionAssignment(member),
                v => throw CollectionSerialize(member));
        }

        private static NeoGenericBinding<NeoReadOnlyDictionary<TEntry>> CreateReadOnlyDictionaryCodec<TEntry>(
            NeoClient client,
            Member member)
        {
            return new NeoGenericBinding<NeoReadOnlyDictionary<TEntry>>(
                MemberKind.Dictionary,
                node =>
                {
                    var dictionaryNode = RequireNode<NeoMemberDictionary>(node, member);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    return new NeoReadOnlyDictionary<TEntry>(
                        client,
                        dictionaryNode,
                        (c, child) =>
                        {
                            entryCodec ??= ResolveForMember<TEntry>(
                                c, dictionaryNode.EntryMember);
                            return entryCodec.Read(child);
                        });
                },
                (node, v) => throw CollectionAssignment(member),
                v => throw CollectionSerialize(member));
        }

        private static NeoGenericBinding<NeoDictionary<TEntry>> CreateDictionaryCodec<TEntry>(
            NeoClient client,
            Member member)
        {
            return new NeoGenericBinding<NeoDictionary<TEntry>>(
                MemberKind.Dictionary,
                node =>
                {
                    var dictionaryNode = RequireNode<NeoMemberDictionary>(node, member);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    NeoGenericBinding<TEntry> EntryCodec()
                    {
                        entryCodec ??= ResolveForMember<TEntry>(
                            client, dictionaryNode.EntryMember);
                        return entryCodec;
                    }
                    return new NeoDictionary<TEntry>(
                        client,
                        dictionaryNode,
                        () => dictionaryNode as NeoMemberDictionaryWritable
                            ?? throw new InvalidOperationException(
                                $"Cannot mutate dictionary generic member '{member.name}' ({member.id}): the node is the read-only NeoMemberDictionary; writes require Save/Session ownership."),
                        (c, child) => EntryCodec().Read(child),
                        item => EntryCodec().Serialize(item));
                },
                (node, v) => throw CollectionAssignment(member),
                v => throw CollectionSerialize(member));
        }

        private static InvalidOperationException CollectionAssignment(Member member)
        {
            return new InvalidOperationException(
                $"Collection generic member '{member.name}' ({member.id}) cannot be assigned as a whole — mutate through the wrapper Read returns (Add/Remove/indexer).");
        }

        private static InvalidOperationException CollectionSerialize(Member member)
        {
            return new InvalidOperationException(
                $"Collection generic member '{member.name}' ({member.id}) cannot be serialized as a single write payload — nested collection entries are populated through their own wrappers.");
        }
    }
}
