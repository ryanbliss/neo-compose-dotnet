// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NeoCompose.Runtime.Json;
using UnityEngine;
using Attribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Read/write codec for one generic-typed member
    /// (specs/custom-type-generics.md Decision 12 / §9). Resolved lazily
    /// per instance from the node's substituted attribute record via
    /// <see cref="NeoGenericBindings.Resolve{T}(NeoClient, NeoAttribute)"/>
    /// and cached in a private field by generated code — no constructor
    /// threading.
    /// </summary>
    public sealed class NeoGenericBinding<T>
    {
        private readonly Func<NeoAttribute, T> read;
        private readonly Action<NeoAttribute, T> write;
        private readonly Func<T, NeoValueWritePayload?> serialize;

        internal NeoGenericBinding(
            AttributeType kind,
            Func<NeoAttribute, T> read,
            Action<NeoAttribute, T> write,
            Func<T, NeoValueWritePayload?> serialize)
        {
            Kind = kind;
            this.read = read;
            this.write = write;
            this.serialize = serialize;
        }

        /// <summary>The substituted runtime attribute kind this codec projects.</summary>
        public AttributeType Kind { get; }

        /// <summary>Reads the member's current value off the child node.</summary>
        public T Read(NeoAttribute node) => read(node);

        /// <summary>
        /// Writes <paramref name="value"/> through the child node's writable
        /// surface. Throws when the node is a read-only kind, and for
        /// collection kinds (collections mutate through the wrapper
        /// <see cref="Read"/> returns, never by whole-value assignment).
        /// </summary>
        public void Write(NeoAttribute node, T value) => write(node, value);

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
    /// attribute records (specs/custom-type-generics.md §9). This is the
    /// runtime half of the cross-repo contract documented above
    /// <c>resolveGenericScopeForEmission</c> in the web codegen
    /// (<c>generate-unity-types.ts</c>) — the emitted call surface is:
    ///
    /// <code>
    /// private NeoGenericBinding&lt;T&gt;? _speedGenericBinding;
    /// public T Speed
    /// {
    ///     get
    ///     {
    ///         var child = node.Get&lt;NeoAttribute&gt;("Speed");
    ///         _speedGenericBinding ??= NeoGenericBindings.Resolve&lt;T&gt;(client, child);
    ///         return _speedGenericBinding.Read(child);
    ///     }
    ///     set
    ///     {
    ///         var child = writableNode.Get&lt;NeoAttribute&gt;("Speed");
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
    /// (stamp-substituted) entry attribute.</para>
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
    ///   <item><description>String → <c>string</c> (localizable strings read
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
    ///   <item><description>Enum (multiselect) →
    ///   <c>IReadOnlyList&lt;Wrapper&gt;</c> or raw
    ///   <c>string[]</c></description></item>
    ///   <item><description>Custom → the generated class; dispatched through
    ///   the class's generated <c>Create</c>/<c>CreateWritable</c> factory
    ///   (reflected once per <c>T</c>), matching the registry+checked-cast
    ///   semantics constructed-Custom slots use</description></item>
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
    /// naming the attribute, the substituted kind's expected projection,
    /// and the actual generic argument.</para>
    /// </summary>
    public static class NeoGenericBindings
    {
        /// <summary>
        /// Resolves the codec for <paramref name="node"/>. For scalar slots
        /// the node's own (already substituted) attribute record decides the
        /// kind; for a collection node whose own wrapper projection does not
        /// match <typeparamref name="T"/>, resolution targets the node's
        /// stamp-substituted ENTRY attribute (the saved-wrapper serializer
        /// hook shape).
        /// </summary>
        public static NeoGenericBinding<T> Resolve<T>(NeoClient client, NeoAttribute node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(
                    nameof(node),
                    "NeoGenericBindings.Resolve requires the member's child node.");
            }
            if (node is NeoAttributeList listNode
                && !TargetsOwnCollectionWrapper<T>(client, listNode.EntryAttribute, isList: true))
            {
                return ResolveForAttribute<T>(client, listNode.EntryAttribute);
            }
            if (node is NeoAttributeDictionary dictionaryNode
                && !TargetsOwnCollectionWrapper<T>(client, dictionaryNode.EntryAttribute, isList: false))
            {
                return ResolveForAttribute<T>(client, dictionaryNode.EntryAttribute);
            }
            return ResolveForAttribute<T>(client, node.attribute);
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
            Attribute entryAttribute,
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
            bool entryIsCollection = entryAttribute is ListAttribute
                || entryAttribute is DictionaryAttribute;
            return !entryIsCollection;
        }

        /// <summary>
        /// Record-based core — resolves a codec from a substituted attribute
        /// record directly (used for collection entry codecs, where the
        /// entry attribute is known before any entry node exists).
        /// </summary>
        internal static NeoGenericBinding<T> ResolveForAttribute<T>(
            NeoClient client,
            Attribute attribute)
        {
            switch (attribute.type)
            {
                case AttributeType.Bool: return BoolCodec<T>(attribute);
                case AttributeType.Int: return IntCodec<T>(attribute);
                case AttributeType.Float: return FloatCodec<T>(attribute);
                case AttributeType.Decimal: return DecimalCodec<T>(attribute);
                case AttributeType.String: return StringCodec<T>(attribute);
                case AttributeType.Color: return ColorCodec<T>(attribute);
                case AttributeType.Vector2: return Vector2Codec<T>(attribute);
                case AttributeType.Vector2Int: return Vector2IntCodec<T>(attribute);
                case AttributeType.Vector3: return Vector3Codec<T>(attribute);
                case AttributeType.Vector3Int: return Vector3IntCodec<T>(attribute);
                case AttributeType.Sprite: return SpriteCodec<T>(client, attribute);
                case AttributeType.Audio: return AudioCodec<T>(client, attribute);
                case AttributeType.Enum: return EnumCodec<T>(attribute);
                case AttributeType.Custom: return CustomCodec<T>(client, attribute);
                case AttributeType.List: return ListCodec<T>(client, attribute);
                case AttributeType.Dictionary: return DictionaryCodec<T>(client, attribute);
                case AttributeType.Generic:
                    throw new InvalidOperationException(
                        $"NeoGenericBindings.Resolve received the un-substituted Generic slot '{attribute.name}' ({attribute.id}) — substitution must run before codec resolution (is the node's parent a Custom node with a closed type context?).");
                default:
                    throw new InvalidOperationException(
                        $"NeoGenericBindings.Resolve: runtime kind {attribute.type} of attribute '{attribute.name}' ({attribute.id}) is not an eligible generic binding kind (spec Decision 14).");
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
            Attribute attribute,
            string expectedProjection)
        {
            return new InvalidOperationException(
                $"Generic binding mismatch on attribute '{attribute.name}' ({attribute.id}): the substituted runtime kind {attribute.type} projects '{expectedProjection}', but the generic argument is '{typeof(T).FullName}'.");
        }

        private static InvalidOperationException MissingValue<T>(Attribute attribute)
        {
            return new InvalidOperationException(
                $"Generic member '{attribute.name}' ({attribute.id}) has no stored value, but the generic argument '{typeof(T).FullName}' is non-nullable.");
        }

        private static TNode RequireNode<TNode>(NeoAttribute node, Attribute attribute)
            where TNode : NeoAttribute
        {
            if (node is TNode match) return match;
            throw new InvalidOperationException(
                $"Generic member '{attribute.name}' ({attribute.id}) resolved a {node.GetType().Name} node where a {typeof(TNode).Name} was expected — the node was constructed from a different substitution than this codec.");
        }

        private static TNode RequireWritable<TNode>(NeoAttribute node, Attribute attribute)
            where TNode : NeoAttribute
        {
            if (node is TNode match) return match;
            throw new InvalidOperationException(
                $"Cannot write generic member '{attribute.name}' ({attribute.id}): the node is the read-only {node.GetType().Name}; writes require the {typeof(TNode).Name} constructed under Save/Session ownership.");
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

        private static NeoGenericBinding<T> BoolCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(bool?))
            {
                return Adapt<T, bool?>(new NeoGenericBinding<bool?>(
                    AttributeType.Bool,
                    node => RequireNode<NeoAttributeBool>(node, attribute).value?.value,
                    (node, v) => RequireWritable<NeoAttributeBoolWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            if (typeof(T) == typeof(bool))
            {
                return Adapt<T, bool>(new NeoGenericBinding<bool>(
                    AttributeType.Bool,
                    node => RequireNode<NeoAttributeBool>(node, attribute).value?.value
                        ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeBoolWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            throw Mismatch<T>(attribute, "bool' or 'bool?");
        }

        private static NeoGenericBinding<T> IntCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(int?))
            {
                return Adapt<T, int?>(new NeoGenericBinding<int?>(
                    AttributeType.Int,
                    node => ReadInt(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeIntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(v.HasValue ? (double?)v.Value : null)));
            }
            if (typeof(T) == typeof(int))
            {
                return Adapt<T, int>(new NeoGenericBinding<int>(
                    AttributeType.Int,
                    node => ReadInt(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeIntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue((double)v)));
            }
            throw Mismatch<T>(attribute, "int' or 'int?");
        }

        private static int? ReadInt(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeInt>(node, attribute).value?.value;
            return raw.HasValue ? (int)raw.Value : null;
        }

        private static NeoGenericBinding<T> FloatCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(double?))
            {
                return Adapt<T, double?>(new NeoGenericBinding<double?>(
                    AttributeType.Float,
                    node => RequireNode<NeoAttributeFloat>(node, attribute).value?.value,
                    (node, v) => RequireWritable<NeoAttributeFloatWritable>(node, attribute)
                        .Set(v.HasValue ? (float?)v.Value : null),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            if (typeof(T) == typeof(double))
            {
                return Adapt<T, double>(new NeoGenericBinding<double>(
                    AttributeType.Float,
                    node => RequireNode<NeoAttributeFloat>(node, attribute).value?.value
                        ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeFloatWritable>(node, attribute)
                        .Set((float)v),
                    v => NeoValueWritePayload.FromValue(v)));
            }
            throw Mismatch<T>(attribute, "double' or 'double?");
        }

        private static NeoGenericBinding<T> DecimalCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(decimal?))
            {
                return Adapt<T, decimal?>(new NeoGenericBinding<decimal?>(
                    AttributeType.Decimal,
                    node => NeoDecimalValues.ParseOrNull(
                        RequireNode<NeoAttributeDecimal>(node, attribute).value?.value),
                    (node, v) => RequireWritable<NeoAttributeDecimalWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoDecimalValues.FormatOrNull(v))));
            }
            if (typeof(T) == typeof(decimal))
            {
                return Adapt<T, decimal>(new NeoGenericBinding<decimal>(
                    AttributeType.Decimal,
                    node => NeoDecimalValues.ParseOrNull(
                        RequireNode<NeoAttributeDecimal>(node, attribute).value?.value)
                        ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeDecimalWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoDecimalValues.Format(v))));
            }
            throw Mismatch<T>(attribute, "decimal' or 'decimal?");
        }

        private static NeoGenericBinding<T> StringCodec<T>(Attribute attribute)
        {
            if (typeof(T) != typeof(string))
            {
                throw Mismatch<T>(attribute, "string");
            }
            bool localizable = attribute is StringAttribute stringAttribute
                && stringAttribute.localizable;
            return Adapt<T, string?>(new NeoGenericBinding<string?>(
                AttributeType.String,
                node =>
                {
                    var stringNode = RequireNode<NeoAttributeString>(node, attribute);
                    string? text = localizable ? stringNode.Text : stringNode.value?.value;
                    if (text is null && attribute.required)
                    {
                        throw new InvalidOperationException(
                            $"Required string '{attribute.name}' ({attribute.id}) has no value.");
                    }
                    return text;
                },
                (node, v) => RequireWritable<NeoAttributeStringWritable>(node, attribute).Set(v),
                v => NeoValueWritePayload.FromValue(v)));
        }

        // ------------------------------------------------------------------
        // Color codec — codegen projects the bound wrapper family
        // (NeoReadOnlyColor read-only / NeoColor saved); the native Color
        // forms are additionally accepted for hand-written call sites.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> ColorCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(NeoColor))
            {
                return Adapt<T, NeoColor?>(new NeoGenericBinding<NeoColor?>(
                    AttributeType.Color,
                    node =>
                    {
                        var colorNode = RequireNode<NeoAttributeColor>(node, attribute);
                        return colorNode.value?.value is null ? null : new NeoColor(colorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeColorWritable>(node, attribute)
                        .Set(v is null ? (Color?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.ColorValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyColor))
            {
                return Adapt<T, NeoReadOnlyColor?>(new NeoGenericBinding<NeoReadOnlyColor?>(
                    AttributeType.Color,
                    node =>
                    {
                        var colorNode = RequireNode<NeoAttributeColor>(node, attribute);
                        return colorNode.value?.value is null ? null : new NeoReadOnlyColor(colorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeColorWritable>(node, attribute)
                        .Set(v is null ? (Color?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.ColorValue(v.Value))));
            }
            if (typeof(T) == typeof(Color?))
            {
                return Adapt<T, Color?>(new NeoGenericBinding<Color?>(
                    AttributeType.Color,
                    node => ReadColor(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeColorWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.ColorValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Color))
            {
                return Adapt<T, Color>(new NeoGenericBinding<Color>(
                    AttributeType.Color,
                    node => ReadColor(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeColorWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.ColorValue(v))));
            }
            throw Mismatch<T>(attribute, "NeoReadOnlyColor', 'NeoColor', 'Color' or 'Color?");
        }

        private static Color? ReadColor(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeColor>(node, attribute).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadColorValue(raw);
        }

        // ------------------------------------------------------------------
        // Vector codecs — same wrapper-family projection as Color.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> Vector2Codec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(NeoVector2))
            {
                return Adapt<T, NeoVector2?>(new NeoGenericBinding<NeoVector2?>(
                    AttributeType.Vector2,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector2>(node, attribute);
                        return vectorNode.value?.value is null ? null : new NeoVector2(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector2Writable>(node, attribute)
                        .Set(v is null ? (Vector2?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2Value(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector2))
            {
                return Adapt<T, NeoReadOnlyVector2?>(new NeoGenericBinding<NeoReadOnlyVector2?>(
                    AttributeType.Vector2,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector2>(node, attribute);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector2(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector2Writable>(node, attribute)
                        .Set(v is null ? (Vector2?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2Value(v.Value))));
            }
            if (typeof(T) == typeof(Vector2?))
            {
                return Adapt<T, Vector2?>(new NeoGenericBinding<Vector2?>(
                    AttributeType.Vector2,
                    node => ReadVector2(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeVector2Writable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector2Value(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector2))
            {
                return Adapt<T, Vector2>(new NeoGenericBinding<Vector2>(
                    AttributeType.Vector2,
                    node => ReadVector2(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeVector2Writable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector2Value(v))));
            }
            throw Mismatch<T>(
                attribute,
                "NeoReadOnlyVector2', 'NeoVector2', 'Vector2' or 'Vector2?");
        }

        private static Vector2? ReadVector2(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeVector2>(node, attribute).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector2Value(raw);
        }

        private static NeoGenericBinding<T> Vector2IntCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(NeoVector2Int))
            {
                return Adapt<T, NeoVector2Int?>(new NeoGenericBinding<NeoVector2Int?>(
                    AttributeType.Vector2Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector2Int>(node, attribute);
                        return vectorNode.value?.value is null ? null : new NeoVector2Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector2IntWritable>(node, attribute)
                        .Set(v is null ? (Vector2Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2IntValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector2Int))
            {
                return Adapt<T, NeoReadOnlyVector2Int?>(new NeoGenericBinding<NeoReadOnlyVector2Int?>(
                    AttributeType.Vector2Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector2Int>(node, attribute);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector2Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector2IntWritable>(node, attribute)
                        .Set(v is null ? (Vector2Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector2IntValue(v.Value))));
            }
            if (typeof(T) == typeof(Vector2Int?))
            {
                return Adapt<T, Vector2Int?>(new NeoGenericBinding<Vector2Int?>(
                    AttributeType.Vector2Int,
                    node => ReadVector2Int(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeVector2IntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector2IntValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector2Int))
            {
                return Adapt<T, Vector2Int>(new NeoGenericBinding<Vector2Int>(
                    AttributeType.Vector2Int,
                    node => ReadVector2Int(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeVector2IntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector2IntValue(v))));
            }
            throw Mismatch<T>(
                attribute,
                "NeoReadOnlyVector2Int', 'NeoVector2Int', 'Vector2Int' or 'Vector2Int?");
        }

        private static Vector2Int? ReadVector2Int(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeVector2Int>(node, attribute).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector2IntValue(raw);
        }

        private static NeoGenericBinding<T> Vector3Codec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(NeoVector3))
            {
                return Adapt<T, NeoVector3?>(new NeoGenericBinding<NeoVector3?>(
                    AttributeType.Vector3,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector3>(node, attribute);
                        return vectorNode.value?.value is null ? null : new NeoVector3(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector3Writable>(node, attribute)
                        .Set(v is null ? (Vector3?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3Value(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector3))
            {
                return Adapt<T, NeoReadOnlyVector3?>(new NeoGenericBinding<NeoReadOnlyVector3?>(
                    AttributeType.Vector3,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector3>(node, attribute);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector3(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector3Writable>(node, attribute)
                        .Set(v is null ? (Vector3?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3Value(v.Value))));
            }
            if (typeof(T) == typeof(Vector3?))
            {
                return Adapt<T, Vector3?>(new NeoGenericBinding<Vector3?>(
                    AttributeType.Vector3,
                    node => ReadVector3(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeVector3Writable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector3Value(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector3))
            {
                return Adapt<T, Vector3>(new NeoGenericBinding<Vector3>(
                    AttributeType.Vector3,
                    node => ReadVector3(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeVector3Writable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector3Value(v))));
            }
            throw Mismatch<T>(
                attribute,
                "NeoReadOnlyVector3', 'NeoVector3', 'Vector3' or 'Vector3?");
        }

        private static Vector3? ReadVector3(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeVector3>(node, attribute).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector3Value(raw);
        }

        private static NeoGenericBinding<T> Vector3IntCodec<T>(Attribute attribute)
        {
            if (typeof(T) == typeof(NeoVector3Int))
            {
                return Adapt<T, NeoVector3Int?>(new NeoGenericBinding<NeoVector3Int?>(
                    AttributeType.Vector3Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector3Int>(node, attribute);
                        return vectorNode.value?.value is null ? null : new NeoVector3Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector3IntWritable>(node, attribute)
                        .Set(v is null ? (Vector3Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3IntValue(v.Value))));
            }
            if (typeof(T) == typeof(NeoReadOnlyVector3Int))
            {
                return Adapt<T, NeoReadOnlyVector3Int?>(new NeoGenericBinding<NeoReadOnlyVector3Int?>(
                    AttributeType.Vector3Int,
                    node =>
                    {
                        var vectorNode = RequireNode<NeoAttributeVector3Int>(node, attribute);
                        return vectorNode.value?.value is null
                            ? null
                            : new NeoReadOnlyVector3Int(vectorNode);
                    },
                    (node, v) => RequireWritable<NeoAttributeVector3IntWritable>(node, attribute)
                        .Set(v is null ? (Vector3Int?)null : v.Value),
                    v => NeoValueWritePayload.FromValue(
                        v is null ? null : NeoGeneratedTypesSupport.Vector3IntValue(v.Value))));
            }
            if (typeof(T) == typeof(Vector3Int?))
            {
                return Adapt<T, Vector3Int?>(new NeoGenericBinding<Vector3Int?>(
                    AttributeType.Vector3Int,
                    node => ReadVector3Int(node, attribute),
                    (node, v) => RequireWritable<NeoAttributeVector3IntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(
                        v.HasValue ? NeoGeneratedTypesSupport.Vector3IntValue(v.Value) : null)));
            }
            if (typeof(T) == typeof(Vector3Int))
            {
                return Adapt<T, Vector3Int>(new NeoGenericBinding<Vector3Int>(
                    AttributeType.Vector3Int,
                    node => ReadVector3Int(node, attribute) ?? throw MissingValue<T>(attribute),
                    (node, v) => RequireWritable<NeoAttributeVector3IntWritable>(node, attribute).Set(v),
                    v => NeoValueWritePayload.FromValue(NeoGeneratedTypesSupport.Vector3IntValue(v))));
            }
            throw Mismatch<T>(
                attribute,
                "NeoReadOnlyVector3Int', 'NeoVector3Int', 'Vector3Int' or 'Vector3Int?");
        }

        private static Vector3Int? ReadVector3Int(NeoAttribute node, Attribute attribute)
        {
            var raw = RequireNode<NeoAttributeVector3Int>(node, attribute).value?.value;
            return raw is null ? null : NeoGeneratedTypesSupport.ReadVector3IntValue(raw);
        }

        // ------------------------------------------------------------------
        // File codecs.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> SpriteCodec<T>(NeoClient client, Attribute attribute)
        {
            if (typeof(T) != typeof(Sprite))
            {
                throw Mismatch<T>(attribute, "UnityEngine.Sprite");
            }
            return Adapt<T, Sprite?>(new NeoGenericBinding<Sprite?>(
                AttributeType.Sprite,
                node =>
                {
                    var resolved = RequireNode<NeoAttributeSprite>(node, attribute).Resolve();
                    if (resolved is null && attribute.required)
                    {
                        throw new InvalidOperationException(
                            $"Required Sprite '{attribute.name}' ({attribute.id}) has no synchronized asset.");
                    }
                    return resolved;
                },
                (node, v) => RequireWritable<NeoAttributeSpriteWritable>(node, attribute).Set(v),
                v => NeoValueWritePayload.FromValue(
                    NeoGeneratedTypesSupport.SpriteValue(client, v, null, attribute.name))));
        }

        private static NeoGenericBinding<T> AudioCodec<T>(NeoClient client, Attribute attribute)
        {
            if (typeof(T) != typeof(AudioClip))
            {
                throw Mismatch<T>(attribute, "UnityEngine.AudioClip");
            }
            return Adapt<T, AudioClip?>(new NeoGenericBinding<AudioClip?>(
                AttributeType.Audio,
                node =>
                {
                    var resolved = RequireNode<NeoAttributeAudio>(node, attribute).Resolve();
                    if (resolved is null && attribute.required)
                    {
                        throw new InvalidOperationException(
                            $"Required Audio '{attribute.name}' ({attribute.id}) has no synchronized asset.");
                    }
                    return resolved;
                },
                (node, v) => RequireWritable<NeoAttributeAudioWritable>(node, attribute).Set(v),
                v => NeoValueWritePayload.FromValue(
                    NeoGeneratedTypesSupport.AudioValue(client, v, null, attribute.name))));
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

        private static NeoGenericBinding<T> EnumCodec<T>(Attribute attribute)
        {
            bool multiselect = attribute is EnumAttribute enumAttribute && enumAttribute.multiselect;
            if (multiselect)
            {
                if (typeof(T) == typeof(string[]))
                {
                    return Adapt<T, string[]>(new NeoGenericBinding<string[]>(
                        AttributeType.Enum,
                        node => RequireNode<NeoAttributeEnum>(node, attribute).Selected(),
                        (node, v) => RequireWritable<NeoAttributeEnumWritable>(node, attribute).Set(v),
                        v => NeoValueWritePayload.FromValue(v)));
                }
                if (typeof(T).IsGenericType
                    && typeof(T).GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                {
                    return InvokeGenericCore<T>(
                        attribute,
                        nameof(CreateEnumListCodec),
                        typeof(T).GetGenericArguments()[0],
                        new object?[] { attribute });
                }
                throw Mismatch<T>(
                    attribute,
                    "IReadOnlyList<Wrapper>' (generated enum wrappers) or 'string[]' (option ids)");
            }
            if (typeof(T) == typeof(string))
            {
                return Adapt<T, string?>(new NeoGenericBinding<string?>(
                    AttributeType.Enum,
                    node => NeoGeneratedTypesSupport.ReadSingleSelected(
                        RequireNode<NeoAttributeEnum>(node, attribute)),
                    (node, v) => RequireWritable<NeoAttributeEnumWritable>(node, attribute)
                        .Set(v is null ? null : new[] { v }),
                    v => NeoValueWritePayload.FromValue(v is null ? null : new[] { v })));
            }
            var fromOptionId = EnumWrapperOps<T>.FromOptionId;
            if (fromOptionId is null)
            {
                throw Mismatch<T>(
                    attribute,
                    "a generated enum wrapper with an implicit string→wrapper conversion, or 'string");
            }
            var toOptionId = EnumWrapperOps<T>.ToOptionId;
            if (toOptionId is null)
            {
                throw Mismatch<T>(
                    attribute,
                    "a generated enum wrapper with an implicit wrapper→string conversion, or 'string");
            }
            return new NeoGenericBinding<T>(
                AttributeType.Enum,
                node =>
                {
                    string? selected = NeoGeneratedTypesSupport.ReadSingleSelected(
                        RequireNode<NeoAttributeEnum>(node, attribute));
                    return selected is null ? default! : fromOptionId(selected);
                },
                (node, v) => RequireWritable<NeoAttributeEnumWritable>(node, attribute)
                    .Set(v is null ? null : new[] { toOptionId(v) }),
                v => NeoValueWritePayload.FromValue(v is null ? null : new[] { toOptionId(v) }));
        }

        private static NeoGenericBinding<IReadOnlyList<TWrapper>> CreateEnumListCodec<TWrapper>(
            Attribute attribute)
        {
            var fromOptionId = EnumWrapperOps<TWrapper>.FromOptionId;
            if (fromOptionId is null)
            {
                throw Mismatch<IReadOnlyList<TWrapper>>(
                    attribute,
                    "IReadOnlyList<Wrapper>' where Wrapper carries an implicit string→wrapper conversion");
            }
            var toOptionId = EnumWrapperOps<TWrapper>.ToOptionId;
            if (toOptionId is null)
            {
                throw Mismatch<IReadOnlyList<TWrapper>>(
                    attribute,
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
                AttributeType.Enum,
                node => NeoGeneratedTypesSupport.ReadEnumList(
                    RequireNode<NeoAttributeEnum>(node, attribute).Selected(),
                    fromOptionId),
                (node, v) => RequireWritable<NeoAttributeEnumWritable>(node, attribute)
                    .Set(v is null ? null : ToOptionIds(v)),
                v => NeoValueWritePayload.FromValue(v is null ? null : ToOptionIds(v)));
        }

        // ------------------------------------------------------------------
        // Custom codec — dispatches through the generated per-class
        // Create/CreateWritable factories (reflected once per T), the same
        // registry+checked-cast semantics constructed-Custom slots use
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
                Create = FindFactory("Create", typeof(NeoAttributeCustom), flags);
                CreateWritable = FindFactory(
                    "CreateWritable", typeof(NeoAttributeCustomWritable), flags);
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

        private static NeoGenericBinding<T> CustomCodec<T>(NeoClient client, Attribute attribute)
        {
            if (attribute is not CustomAttribute customAttribute)
            {
                throw new InvalidOperationException(
                    $"NeoGenericBindings: attribute '{attribute.name}' ({attribute.id}) reports kind Custom but is a {attribute.GetType().Name} record — the export is corrupt.");
            }
            if (GeneratedFactoryOps<T>.Create is null
                && GeneratedFactoryOps<T>.CreateWritable is null)
            {
                throw Mismatch<T>(
                    attribute,
                    $"a generated class for custom type '{customAttribute.customTypeId}' exposing the generated Create/CreateWritable factory");
            }
            return new NeoGenericBinding<T>(
                AttributeType.Custom,
                node =>
                {
                    var customNode = RequireNode<NeoAttributeCustom>(node, attribute);
                    if (customNode.value is null)
                    {
                        if (attribute.required)
                        {
                            throw new InvalidOperationException(
                                $"Required custom '{attribute.name}' ({attribute.id}) has no value.");
                        }
                        return default!;
                    }
                    object? resolved;
                    if (customNode is NeoAttributeCustomWritable writableNode
                        && GeneratedFactoryOps<T>.CreateWritable is not null)
                    {
                        resolved = GeneratedFactoryOps<T>.CreateWritable
                            .Invoke(null, new object[] { client, writableNode });
                    }
                    else if (GeneratedFactoryOps<T>.Create is not null)
                    {
                        resolved = GeneratedFactoryOps<T>.Create
                            .Invoke(null, new object[] { client, customNode });
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Custom generic member '{attribute.name}' ({attribute.id}) resolved a read-only node, but '{typeof(T).FullName}' only exposes CreateWritable.");
                    }
                    if (resolved is not T typed)
                    {
                        throw new InvalidOperationException(
                            $"Custom generic member '{attribute.name}' ({attribute.id}) resolved a '{resolved?.GetType().FullName ?? "null"}', which is not the generic argument '{typeof(T).FullName}' — the stored value's typeId does not match the closed construction (authoring-time signature validation should make this unreachable).");
                    }
                    return typed;
                },
                (node, v) =>
                {
                    if (node.parent is not NeoAttributeCustomWritable parentRecord)
                    {
                        throw new InvalidOperationException(
                            $"Cannot assign custom generic member '{attribute.name}' ({attribute.id}): the node has no writable Custom parent to rebind through.");
                    }
                    if (!parentRecord.TryGetSchemaKeyForChild(node, out string? schemaKey))
                    {
                        throw new InvalidOperationException(
                            $"Cannot assign custom generic member '{attribute.name}' ({attribute.id}): the node is not a registered schema field of its parent.");
                    }
                    parentRecord.SetSerializedValue(schemaKey, SerializeCustom(attribute, v));
                },
                v => SerializeCustom(attribute, v));
        }

        private static NeoValueWritePayload? SerializeCustom<T>(Attribute attribute, T value)
        {
            if (value is null) return null;
            if (value is INeoValueReference reference)
            {
                return NeoGeneratedTypesSupport.ValueReference(reference);
            }
            throw new InvalidOperationException(
                $"Cannot serialize '{value.GetType().FullName}' for custom generic member '{attribute.name}' ({attribute.id}): the value does not expose a backing value id (INeoValueReference).");
        }

        // ------------------------------------------------------------------
        // Collection codecs — T is a closed NeoReadOnlyList<>/NeoList<>/
        // NeoReadOnlyDictionary<>/NeoDictionary<> construction; the entry
        // codec resolves recursively from the collection node's substituted
        // entry attribute.
        // ------------------------------------------------------------------

        private static NeoGenericBinding<T> ListCodec<T>(NeoClient client, Attribute attribute)
        {
            Type target = typeof(T);
            if (!target.IsGenericType)
            {
                throw Mismatch<T>(attribute, "NeoReadOnlyList<TEntry>' or 'NeoList<TEntry>");
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
                throw Mismatch<T>(attribute, "NeoReadOnlyList<TEntry>' or 'NeoList<TEntry>");
            }
            return InvokeGenericCore<T>(
                attribute,
                coreName,
                target.GetGenericArguments()[0],
                new object?[] { client, attribute });
        }

        private static NeoGenericBinding<T> DictionaryCodec<T>(NeoClient client, Attribute attribute)
        {
            Type target = typeof(T);
            if (!target.IsGenericType)
            {
                throw Mismatch<T>(attribute, "NeoReadOnlyDictionary<TEntry>' or 'NeoDictionary<TEntry>");
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
                    $"Generic binding mismatch on attribute '{attribute.name}' ({attribute.id}): enum-keyed (two-arity) dictionary wrappers are not supported as generic bindings yet — use the single-arity NeoReadOnlyDictionary<TEntry> view.");
            }
            else if (definition == typeof(NeoDictionary<,>))
            {
                throw new InvalidOperationException(
                    $"Generic binding mismatch on attribute '{attribute.name}' ({attribute.id}): enum-keyed (two-arity) dictionary wrappers are not supported as generic bindings yet — use the single-arity NeoDictionary<TEntry> view.");
            }
            else
            {
                throw Mismatch<T>(attribute, "NeoReadOnlyDictionary<TEntry>' or 'NeoDictionary<TEntry>");
            }
            return InvokeGenericCore<T>(
                attribute,
                coreName,
                target.GetGenericArguments()[0],
                new object?[] { client, attribute });
        }

        private static NeoGenericBinding<T> InvokeGenericCore<T>(
            Attribute attribute,
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
                    $"NeoGenericBindings is missing its codec core '{coreName}' — this is an SDK bug (attribute '{attribute.id}').");
            }
            object? codec = core.MakeGenericMethod(typeArgument).Invoke(null, arguments);
            return (NeoGenericBinding<T>)codec!;
        }

        private static NeoGenericBinding<NeoReadOnlyList<TEntry>> CreateReadOnlyListCodec<TEntry>(
            NeoClient client,
            Attribute attribute)
        {
            return new NeoGenericBinding<NeoReadOnlyList<TEntry>>(
                AttributeType.List,
                node =>
                {
                    var listNode = RequireNode<NeoAttributeList>(node, attribute);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    return new NeoReadOnlyList<TEntry>(
                        client,
                        listNode,
                        (c, child) =>
                        {
                            entryCodec ??= ResolveForAttribute<TEntry>(c, listNode.EntryAttribute);
                            return entryCodec.Read(child);
                        });
                },
                (node, v) => throw CollectionAssignment(attribute),
                v => throw CollectionSerialize(attribute));
        }

        private static NeoGenericBinding<NeoList<TEntry>> CreateListCodec<TEntry>(
            NeoClient client,
            Attribute attribute)
        {
            return new NeoGenericBinding<NeoList<TEntry>>(
                AttributeType.List,
                node =>
                {
                    var listNode = RequireNode<NeoAttributeList>(node, attribute);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    NeoGenericBinding<TEntry> EntryCodec()
                    {
                        entryCodec ??= ResolveForAttribute<TEntry>(client, listNode.EntryAttribute);
                        return entryCodec;
                    }
                    return new NeoList<TEntry>(
                        client,
                        listNode,
                        () => listNode as NeoAttributeListWritable
                            ?? throw new InvalidOperationException(
                                $"Cannot mutate list generic member '{attribute.name}' ({attribute.id}): the node is the read-only NeoAttributeList; writes require Save/Session ownership."),
                        (c, child) => EntryCodec().Read(child),
                        item => EntryCodec().Serialize(item));
                },
                (node, v) => throw CollectionAssignment(attribute),
                v => throw CollectionSerialize(attribute));
        }

        private static NeoGenericBinding<NeoReadOnlyDictionary<TEntry>> CreateReadOnlyDictionaryCodec<TEntry>(
            NeoClient client,
            Attribute attribute)
        {
            return new NeoGenericBinding<NeoReadOnlyDictionary<TEntry>>(
                AttributeType.Dictionary,
                node =>
                {
                    var dictionaryNode = RequireNode<NeoAttributeDictionary>(node, attribute);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    return new NeoReadOnlyDictionary<TEntry>(
                        client,
                        dictionaryNode,
                        (c, child) =>
                        {
                            entryCodec ??= ResolveForAttribute<TEntry>(
                                c, dictionaryNode.EntryAttribute);
                            return entryCodec.Read(child);
                        });
                },
                (node, v) => throw CollectionAssignment(attribute),
                v => throw CollectionSerialize(attribute));
        }

        private static NeoGenericBinding<NeoDictionary<TEntry>> CreateDictionaryCodec<TEntry>(
            NeoClient client,
            Attribute attribute)
        {
            return new NeoGenericBinding<NeoDictionary<TEntry>>(
                AttributeType.Dictionary,
                node =>
                {
                    var dictionaryNode = RequireNode<NeoAttributeDictionary>(node, attribute);
                    NeoGenericBinding<TEntry>? entryCodec = null;
                    NeoGenericBinding<TEntry> EntryCodec()
                    {
                        entryCodec ??= ResolveForAttribute<TEntry>(
                            client, dictionaryNode.EntryAttribute);
                        return entryCodec;
                    }
                    return new NeoDictionary<TEntry>(
                        client,
                        dictionaryNode,
                        () => dictionaryNode as NeoAttributeDictionaryWritable
                            ?? throw new InvalidOperationException(
                                $"Cannot mutate dictionary generic member '{attribute.name}' ({attribute.id}): the node is the read-only NeoAttributeDictionary; writes require Save/Session ownership."),
                        (c, child) => EntryCodec().Read(child),
                        item => EntryCodec().Serialize(item));
                },
                (node, v) => throw CollectionAssignment(attribute),
                v => throw CollectionSerialize(attribute));
        }

        private static InvalidOperationException CollectionAssignment(Attribute attribute)
        {
            return new InvalidOperationException(
                $"Collection generic member '{attribute.name}' ({attribute.id}) cannot be assigned as a whole — mutate through the wrapper Read returns (Add/Remove/indexer).");
        }

        private static InvalidOperationException CollectionSerialize(Attribute attribute)
        {
            return new InvalidOperationException(
                $"Collection generic member '{attribute.name}' ({attribute.id}) cannot be serialized as a single write payload — nested collection entries are populated through their own wrappers.");
        }
    }
}
