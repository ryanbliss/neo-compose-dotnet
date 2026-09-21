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
            if (node.value?.value is null) return null;
            return ReadRow(client, node.value, node.ownership, null);
        }

        private static NeoCellPattern ReadRow(NeoClient client, ObjectMemberValue row, NeoValueOwnership ownership, NSGetterEvaluator.Context? ctx)
        {
            var list = client.ResolveClassChildRow(row, "_offsets", ownership) as ArrayMemberValue
                ?? throw new InvalidOperationException("CellPattern offsets are missing.");
            string[] ids = list.value ?? Array.Empty<string>();
            ctx?.allocationTracker.ConsumeCollectionVisit(ids.Length);
            var offsets = new Vector2Int[ids.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                if (!client.TryGetValue(ownership, ids[index], out Vector2MemberValue? vector) || vector.value is null)
                    throw new InvalidOperationException("CellPattern offset is missing.");
                offsets[index] = NeoVectorValues.ToVector2Int(vector.value);
            }
            return new NeoCellPattern(offsets);
        }

        public static NeoCellPattern ReadRequired(NeoClient client, NeoMemberClass node) =>
            Read(client, node) ?? throw new InvalidOperationException("Required CellPattern is missing.");

        public static NeoCellPattern? ReadResult(NeoClient client, object? value, bool required)
        {
            if (value is NeoCellPattern pattern) return pattern;
            return NeoGeneratedTypesSupport.ReadNSPropertyClass(client, value, required, false,
                Read, (c, node) => Read(c, node));
        }

        public static NeoValueWritePayload? Serialize(NeoClient client, NeoCellPattern? pattern)
        {
            if (pattern is null) return null;
            // Use the same declared constructor as NeoScript, including its creation provenance.
            using var node = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, ClassId, ConstructorId,
                new[] { new NeoDeclaredConstructorArgument("offsets", Offsets(pattern)) },
                Array.Empty<NeoGeneratedConstructorValue>());
            return NeoValueWritePayload.FromValueReference(node.value!.id);
        }

        internal static object? NormalizeNativeResult(object? value, NSGetterEvaluator.Context ctx, TypeInfo? type = null)
        {
            if (value is NeoCellPattern pattern) return Materialize(pattern, ctx);
            if (value is NeoCellPatternExcluding excluding) return ExcludingIds(excluding);
            if (value is null || type is not CollectionTypeInfo collection || !ContainsCanonical(collection.entryTypeInfo)) return value;
            if (type.type == MemberKind.List && value is IEnumerable entries)
            {
                var result = new List<object?>();
                foreach (object? entry in entries)
                {
                    ctx.allocationTracker.ConsumeProducedCollectionEntry();
                    result.Add(NormalizeNativeResult(entry, ctx, collection.entryTypeInfo));
                }
                return result.ToArray();
            }
            if (type.type == MemberKind.Dictionary && value is IReadOnlyDictionary<string, object?> dictionary)
            {
                var result = new Dictionary<string, object?>();
                foreach (var entry in dictionary)
                {
                    ctx.allocationTracker.ConsumeProducedCollectionEntry();
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

        internal static object Materialize(NeoCellPattern pattern, NSGetterEvaluator.Context ctx)
        {
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(ctx.client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = ClassId },
                ConstructorId, new[] { "offsets" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
            var constructed = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved,
                new Dictionary<string, object?> { ["offsets"] = Offsets(pattern) },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx);
            ctx.allocationTracker.RegisterSessionRoot(constructed.value.id);
            return NSGetterEvaluator.UnwrapRow(constructed.value, ctx, NeoValueOwnership.Session)!;
        }

        internal static NeoCellPattern ReadRuntime(object? value, NSGetterEvaluator.Context ctx)
        {
            if (value is NeoCellPattern pattern) return pattern;
            string? id = NSGetterEvaluator.FindRowIdByReference(value, ctx);
            var ownership = NSGetterEvaluator.FindRowOwnershipByReference(value, ctx) ?? ctx.valueOwnership;
            if (id is null || !ctx.client.TryGetValue(ownership, id, out ObjectMemberValue? row)
                || row.classId != ClassId)
                throw new NSGetterRuntimeError("Expected a canonical CellPattern value.");
            return ReadRow(ctx.client, row, ownership, ctx);
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
            if (value is NeoCellPatternExcluding excluding) return excluding;
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
