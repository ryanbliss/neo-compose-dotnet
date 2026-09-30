// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Canonical class conversion; patterns themselves remain detached immutable values.</summary>
    public static class NeoCellPatternStorage
    {
        public const string ClassId = "system_fb9c4080-0148-530e-b9c0-8cd1c17ff8e7";
        public const string ConstructorId = "system_f68684d6-6ea0-5d57-9f96-d544b17bf369";
        public const string ExcludingEnumId = "system_072f749f-62fe-5917-9f7e-f9f29c3a7063";
        public const string NoneId = "system_5c30e23b-1767-5e15-b633-40ae182157a7";
        public const string CenterId = "system_456431b5-aa5b-5ea0-9f72-0e16f7114e09";

        public static NeoCellPattern? Read(NeoClient client, NeoMemberClass node)
        {
            if (node.value?.value is null)
                return null;
            return ReadRow(client, node.value, node.ownership, null);
        }

        /// <summary>
        /// A pattern row's last read and the nodes of its offset rows. A read
        /// re-reads every offset through the nodes and keeps the pattern while
        /// the offsets still match it, so a repeat read hashes no ids and
        /// allocates nothing.
        /// </summary>
        private sealed class RowRead
        {
            internal NeoValueNode?[] offsetNodes = Array.Empty<NeoValueNode?>();
            internal NeoCellPattern? pattern;
        }

        /// <param name="node">The row's node, which keeps the read for the next one; null keeps nothing.</param>
        private static NeoCellPattern ReadRow(NeoClient client, ObjectMemberValue row, NeoValueOwnership ownership, NeoValueNode? node)
        {
            var list = client.ResolveClassChildRow(row, "_offsets", ownership) as ArrayMemberValue
                ?? throw new InvalidOperationException("CellPattern offsets are missing.");
            string[] ids = list.value ?? Array.Empty<string>();
            RowRead? read = null;
            if (node is not null)
            {
                read = node.nativeRead as RowRead;
                if (read is null)
                    node.nativeRead = read = new RowRead();
            }
            NeoValueNode?[] offsetNodes = read?.offsetNodes.Length == ids.Length
                ? read.offsetNodes
                : new NeoValueNode?[ids.Length];
            NeoCellPattern? previous = read?.pattern is { } last && last.Count == ids.Length ? last : null;
            Vector2Int[]? offsets = previous is null ? new Vector2Int[ids.Length] : null;
            for (int index = 0; index < ids.Length; index++)
            {
                ref NeoValueNode? offsetNode = ref offsetNodes[index];
                if (offsetNode is not null && !string.Equals(offsetNode.id, ids[index], StringComparison.Ordinal))
                    offsetNode = null;
                client.TryGetValue(ownership, ids[index], ref offsetNode, out MemberValue? offset);
                if (offset is not Vector2MemberValue { value: not null } vector)
                    throw new InvalidOperationException("CellPattern offset is missing.");
                Vector2Int cell = NeoVectorValues.ToVector2Int(vector.value);
                if (offsets is null)
                {
                    if (cell == previous![index])
                        continue;
                    offsets = new Vector2Int[ids.Length];
                    for (int kept = 0; kept < index; kept++)
                        offsets[kept] = previous[kept];
                }
                offsets[index] = cell;
            }
            if (offsets is null)
                return previous!;
            var pattern = NeoCellPattern.FromOwned(offsets);
            if (read is not null)
            {
                read.offsetNodes = offsetNodes;
                read.pattern = pattern;
            }
            return pattern;
        }

        public static NeoCellPattern ReadRequired(NeoClient client, NeoMemberClass node) =>
            Read(client, node) ?? throw new InvalidOperationException("Required CellPattern is missing.");

        public static NeoCellPattern? ReadResult(NeoClient client, object? value, bool required)
        {
            if (value is NeoCellPattern pattern)
                return pattern;
            if (value is NeoScriptObject { attachedId: null } detached && detached.plan.classId == ClassId)
                return ReadDetached(detached);
            return NeoGeneratedTypesSupport.ReadNSPropertyClass(client, value, required, false,
                Read, (c, node) => Read(c, node));
        }

        public static NeoValueWritePayload? Serialize(NeoClient client, NeoCellPattern? pattern)
        {
            if (pattern is null)
                return null;
            // Use the same declared constructor as NeoScript, including its creation provenance.
            using var node = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, ClassId, ConstructorId,
                new[] { new NeoDeclaredConstructorArgument("offsets", Offsets(pattern)) },
                Array.Empty<NeoGeneratedConstructorValue>());
            return NeoValueWritePayload.FromValueReference(node.value!.id);
        }

        internal static object? NormalizeNativeResult(object? value, NSGetterEvaluator.Context ctx, TypeInfo? type = null)
        {
            if (value is NeoCellPattern pattern)
                return Materialize(pattern, ctx);
            if (value is NeoCellPatternExcluding excluding)
                return ExcludingIds(excluding);
            if (value is null || type is not CollectionTypeInfo collection || !ContainsCanonical(collection.entryTypeInfo))
                return value;
            if (type.type == MemberKind.List && value is IEnumerable entries)
            {
                var result = new List<object?>();
                foreach (object? entry in entries)
                {
                    result.Add(NormalizeNativeResult(entry, ctx, collection.entryTypeInfo));
                }
                return result.ToArray();
            }
            if (type.type == MemberKind.Dictionary && value is IReadOnlyDictionary<string, object?> dictionary)
            {
                var result = new Dictionary<string, object?>();
                foreach (var entry in dictionary)
                {
                    result.Add(entry.Key, NormalizeNativeResult(entry.Value, ctx, collection.entryTypeInfo));
                }
                return result;
            }
            return value;
        }

        private static bool ContainsCanonical(TypeInfo type) => type switch
        {
            ClassTypeInfo value => value.classId == ClassId,
            EnumTypeInfo value => value.enumId == ExcludingEnumId,
            CollectionTypeInfo value => ContainsCanonical(value.entryTypeInfo),
            _ => type.type == MemberKind.Generic,
        };

        // The construction site every materialized pattern shares.
        private static readonly object MaterializeSite = new();

        internal static object Materialize(NeoCellPattern pattern, NSGetterEvaluator.Context ctx)
        {
            if (!NeoGeneratedTypesSupport.TryGetResolvedSite(
                    ctx.client, MaterializeSite, out NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved))
            {
                resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(ctx.client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = ClassId },
                    ConstructorId, new[] { "offsets" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
                NeoGeneratedTypesSupport.CacheResolvedSite(ctx.client, MaterializeSite, resolved);
            }
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = Offsets(pattern);
            return NSGetterEvaluator.ConstructDeclared(resolved,
                arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx,
                evaluateFieldValues: null, replayContext: false)!;
        }

        internal static NeoCellPattern ReadRuntime(object? value, NSGetterEvaluator.Context ctx)
        {
            if (value is NeoCellPattern pattern)
                return pattern;
            if (value is NeoScriptObject { attachedId: null } detached && detached.plan.classId == ClassId)
                return ReadDetached(detached);
            NSGetterEvaluator.RowReference? rowRef = NSGetterEvaluator.FindRowReference(value, ctx);
            string? id = rowRef?.valueId;
            var ownership = NSGetterEvaluator.RowOwnership(rowRef, value) ?? ctx.valueOwnership;
            NeoValueNode? node = rowRef?.node;
            if (id is null || !ctx.client.TryGetValue(ownership, id, ref node, out MemberValue? stored)
                || stored is not ObjectMemberValue { classId: ClassId } row)
                throw new NSGetterRuntimeError("Expected a canonical CellPattern value.");
            if (rowRef is not null && !ReferenceEquals(rowRef.node, node))
                rowRef.node = node;
            return ReadRow(ctx.client, row, ownership, node);
        }

        private static NeoCellPattern ReadDetached(NeoScriptObject detached)
        {
            // A constructed pattern never changes, so its first read serves every later one.
            if (detached.nativeValue is NeoCellPattern read)
                return read;
            object?[] entries = detached.plan.slotByKey.TryGetValue("_offsets", out int slot)
                && NeoGeneratedTypesSupport.DetachedArray(detached, slot) is object?[] stored
                    ? stored
                    : throw new InvalidOperationException("CellPattern offsets are missing.");
            var offsets = new Vector2Int[entries.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                offsets[index] = entries[index] is NeoVector2Value vector
                    ? NeoVectorValues.ToVector2Int(vector)
                    : throw new InvalidOperationException("CellPattern offset is missing.");
            }
            var pattern = NeoCellPattern.FromOwned(offsets);
            if (!detached.constructing)
                detached.nativeValue = pattern;
            return pattern;
        }

        private static object?[] Offsets(NeoCellPattern pattern)
        {
            var result = new object?[pattern.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = NeoVectorValues.FromVector2Int(pattern[index]);
            return result;
        }

        public static NeoCellPatternExcluding ReadExcluding(object? value)
        {
            if (value is NeoCellPatternExcluding excluding)
                return excluding;
            string[] ids = NeoGeneratedTypesSupport.ToStringArray(value);
            return ids.Length == 1 ? ids[0] switch
            {
                NoneId => NeoCellPatternExcluding.None,
                CenterId => NeoCellPatternExcluding.Center,
                _ => throw new NSGetterRuntimeError("Unknown CellPattern exclusion."),
            } : throw new NSGetterRuntimeError("Expected one CellPattern exclusion.");
        }

        public static NeoCellPatternExcluding? ReadOptionalExcluding(object? value) =>
            value is NeoCellPatternExcluding excluding ? excluding
                : NeoGeneratedTypesSupport.ToStringArray(value).Length == 0 ? null : ReadExcluding(value);

        public static IReadOnlyList<NeoCellPatternExcluding> ReadExcludingList(object? value) =>
            NeoGeneratedTypesSupport.ToStringArray(value).Select(id => ReadExcluding(new[] { id })).ToArray();

        public static string[]? OptionalExcludingIds(NeoCellPatternExcluding? value) =>
            value.HasValue ? ExcludingIds(value.Value) : null;

        public static string[] ExcludingListIds(IEnumerable<NeoCellPatternExcluding>? values) =>
            values?.Select(value => ExcludingIds(value)[0]).ToArray() ?? Array.Empty<string>();

        public static string[] ExcludingIds(NeoCellPatternExcluding value) => new[]
        {
            value switch
            {
                NeoCellPatternExcluding.None => NoneId,
                NeoCellPatternExcluding.Center => CenterId,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            },
        };
    }
}
