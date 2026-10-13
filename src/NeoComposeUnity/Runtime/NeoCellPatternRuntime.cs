// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoCellPattern's six native helpers. Results are ordinary detached
    /// patterns built by the declared constructor; a no-op returns its receiver.
    /// </summary>
    internal static class NeoCellPatternRuntime
    {
        internal const string ContainsId = "system_65a16908-05a6-52f9-a467-4e37e95ba0be";
        internal const string CellsId = "system_c72d9b09-fc4c-55db-b763-f1954a536069";
        internal const string WithCenterId = "system_1da9303d-c362-541a-a77b-244fb0ffcbb6";
        internal const string WithoutCenterId = "system_7dce7f78-2c8f-5a1f-aa80-89dc217dcdff";
        internal const string TranslateId = "system_efc67858-0c95-573f-a8a9-d7e07d0a1d55";
        internal const string UnionId = "system_77b14581-e34c-5eb1-a820-788618276e46";

        internal static bool Handles(string memberId) => memberId is
            ContainsId or CellsId or WithCenterId or WithoutCenterId or TranslateId or UnionId;

        internal static bool TryInvoke(string memberId, object? receiver, object?[] args, NSGetterEvaluator.Context ctx, out object? result)
        {
            if (!Handles(memberId))
            {
                result = null;
                return false;
            }
            Vector2Int[] offsets = NeoCellPatternOffsets.Read(receiver, ctx);
            switch (memberId)
            {
                case ContainsId:
                    {
                        Vector2Int offset = Vector(args[0], "Contains offset");
                        result = Array.IndexOf(offsets, offset) >= 0;
                        return true;
                    }
                case CellsId:
                    {
                        Vector2Int origin = Vector(args[0], "Cells origin");
                        var cells = new object?[offsets.Length];
                        for (int i = 0; i < offsets.Length; i++)
                            cells[i] = NeoVectorValues.FromVector2Int(Translated(offsets[i], origin));
                        result = cells;
                        return true;
                    }
                case WithCenterId:
                    {
                        if (Array.IndexOf(offsets, Vector2Int.zero) >= 0)
                        {
                            result = receiver;
                            return true;
                        }
                        var cells = new object?[offsets.Length + 1];
                        cells[0] = NeoVectorValues.FromVector2Int(Vector2Int.zero);
                        for (int i = 0; i < offsets.Length; i++)
                            cells[i + 1] = NeoVectorValues.FromVector2Int(offsets[i]);
                        result = Construct(cells, ctx);
                        return true;
                    }
                case WithoutCenterId:
                    {
                        if (Array.IndexOf(offsets, Vector2Int.zero) < 0)
                        {
                            result = receiver;
                            return true;
                        }
                        var cells = new List<object?>(offsets.Length);
                        for (int i = 0; i < offsets.Length; i++)
                        {
                            if (offsets[i] != Vector2Int.zero)
                                cells.Add(NeoVectorValues.FromVector2Int(offsets[i]));
                        }
                        result = Construct(cells.ToArray(), ctx);
                        return true;
                    }
                case TranslateId:
                    {
                        Vector2Int offset = Vector(args[0], "Translate offset");
                        if (offset == Vector2Int.zero)
                        {
                            result = receiver;
                            return true;
                        }
                        var cells = new object?[offsets.Length];
                        for (int i = 0; i < offsets.Length; i++)
                            cells[i] = NeoVectorValues.FromVector2Int(Translated(offsets[i], offset));
                        result = Construct(cells, ctx);
                        return true;
                    }
                case UnionId:
                    {
                        Vector2Int[] other = NeoCellPatternOffsets.Read(args[0], ctx);
                        var seen = new HashSet<Vector2Int>();
                        var cells = new List<object?>(offsets.Length + other.Length);
                        Add(offsets);
                        Add(other);
                        result = Construct(cells.ToArray(), ctx);
                        return true;

                        void Add(Vector2Int[] source)
                        {
                            for (int i = 0; i < source.Length; i++)
                            {
                                if (seen.Add(source[i]))
                                    cells.Add(NeoVectorValues.FromVector2Int(source[i]));
                            }
                        }
                    }
                default:
                    throw new InvalidOperationException($"Cell pattern native member '{memberId}' has no implementation.");
            }
        }

        // The construction site every helper result shares.
        private static readonly object ConstructSite = new();

        private static object Construct(object?[] offsets, NSGetterEvaluator.Context ctx)
        {
            if (!NeoGeneratedTypesSupport.TryGetResolvedSite(
                    ctx.client, ConstructSite, out NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved))
            {
                resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(ctx.client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternOffsets.ClassId },
                    NeoCellPatternOffsets.ConstructorId, new[] { "offsets" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
                NeoGeneratedTypesSupport.CacheResolvedSite(ctx.client, ConstructSite, resolved);
            }
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = offsets;
            return NSGetterEvaluator.ConstructDeclared(resolved,
                arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx,
                evaluateFieldValues: null, replayContext: false)!;
        }

        private static Vector2Int Translated(Vector2Int offset, Vector2Int by) =>
            new(Coordinate((long)offset.x + by.x, "Translated cell.x"), Coordinate((long)offset.y + by.y, "Translated cell.y"));

        private static int Coordinate(long value, string name)
        {
            if (value < int.MinValue)
                throw new NSGetterRuntimeError($"{name} is below the supported integer range.");
            if (value > int.MaxValue)
                throw new NSGetterRuntimeError($"{name} exceeds the supported integer range.");
            return (int)value;
        }

        private static Vector2Int Vector(object? value, string name) =>
            NeoGeneratedTypesSupport.ReadVector2IntValue(value)
                ?? throw new NSGetterRuntimeError($"{name} must be a Vector2Int.");
    }
}
