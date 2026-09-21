// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal static class NeoCellPatternRuntime
    {
        internal static bool TryInvoke(string memberId, object? receiver, object?[] args, NSGetterEvaluator.Context ctx, out object? result, bool materialize = true)
        {
            result = null;
            NeoCellPattern? pattern = null;
            switch (memberId)
            {
                case "system_aaad2df6-e31e-5f5d-95b9-e265211faed5": // Box
                {
                    int x = Int(args[0]), y = args[1] is null ? x : Int(args[1]);
                    var excluding = NeoCellPatternStorage.ReadExcluding(args[2]);
                    ValidateRadii(x, y);
                    Reserve(checked((2L * x + 1) * (2L * y + 1)) - (excluding == NeoCellPatternExcluding.Center ? 1 : 0), ctx);
                    pattern = NeoCellPattern.Box(x, y, excluding);
                    break;
                }
                case "system_df8ebca6-a1d1-57c1-8762-227943fc51d8": // Cross
                {
                    int x = Int(args[0]), y = args[1] is null ? x : Int(args[1]);
                    var excluding = NeoCellPatternStorage.ReadExcluding(args[2]);
                    ValidateRadii(x, y);
                    Reserve(2L * x + 2L * y + (excluding == NeoCellPatternExcluding.Center ? 0 : 1), ctx);
                    pattern = NeoCellPattern.Cross(x, y, excluding);
                    break;
                }
                case "system_82222600-f67f-5127-bb2d-278381b4ef2f": // Ring
                {
                    int radius = Int(args[0]);
                    ValidateRadii(radius, radius);
                    Reserve(radius == 0 ? 1 : 8L * radius, ctx);
                    pattern = NeoCellPattern.Ring(radius);
                    break;
                }
                case "system_d803cfd5-ce53-57da-abc2-7cd69befeb01": // Rect
                {
                    Vector2Int size = Vector(args[0]);
                    if (size.x <= 0 || size.y <= 0) throw new NSGetterRuntimeError("Rect size must be positive.");
                    Reserve((long)size.x * size.y, ctx);
                    pattern = NeoCellPattern.Rect(size);
                    break;
                }
                case "system_ef0d4cbb-7182-5d93-9bb8-60a5a18f81a5": // Line
                {
                    int length = Int(args[1]);
                    if (length < 0) throw new NSGetterRuntimeError("Line length must be nonnegative.");
                    var excluding = NeoCellPatternStorage.ReadExcluding(args[2]);
                    Reserve((long)length + (excluding == NeoCellPatternExcluding.Center ? 0 : 1), ctx);
                    pattern = NeoCellPattern.Line(Vector(args[0]), length, excluding);
                    break;
                }
                case "system_65a16908-05a6-52f9-a467-4e37e95ba0be": // Contains
                {
                    var source = Read(receiver, ctx);
                    result = source.Contains(Vector(args[0]));
                    return true;
                }
                case "system_c72d9b09-fc4c-55db-b763-f1954a536069": // Cells
                {
                    var source = Read(receiver, ctx);
                    Reserve(source.Count, ctx);
                    result = source.GetCells(Vector(args[0])).Select(cell => (object?)NeoVectorValues.FromVector2Int(cell)).ToArray();
                    return true;
                }
                case "system_1da9303d-c362-541a-a77b-244fb0ffcbb6": // WithCenter
                {
                    var source = Read(receiver, ctx);
                    Reserve((long)source.Count + 1, ctx);
                    pattern = source.WithCenter();
                    break;
                }
                case "system_7dce7f78-2c8f-5a1f-aa80-89dc217dcdff": // WithoutCenter
                {
                    var source = Read(receiver, ctx);
                    Reserve(source.Count, ctx);
                    pattern = source.WithoutCenter();
                    break;
                }
                case "system_efc67858-0c95-573f-a8a9-d7e07d0a1d55": // Translate
                {
                    var source = Read(receiver, ctx);
                    Reserve(source.Count, ctx);
                    pattern = source.Translate(Vector(args[0]));
                    break;
                }
                case "system_77b14581-e34c-5eb1-a820-788618276e46": // Union
                {
                    var source = Read(receiver, ctx);
                    var other = Read(args[0], ctx);
                    Reserve((long)source.Count + other.Count, ctx);
                    pattern = source.Union(other);
                    break;
                }
                default: return false;
            }
            result = materialize ? NeoCellPatternStorage.Materialize(pattern!, ctx) : pattern;
            return true;
        }

        // A pattern produced directly for a grid query cannot escape into
        // storage or expose its temporary row identity. Keep that intermediate
        // as offsets; ordinary class-producing calls still use the constructor.
        internal static bool ProducesPattern(string? memberId) => memberId is
            "system_aaad2df6-e31e-5f5d-95b9-e265211faed5" or // Box
            "system_df8ebca6-a1d1-57c1-8762-227943fc51d8" or // Cross
            "system_82222600-f67f-5127-bb2d-278381b4ef2f" or // Ring
            "system_d803cfd5-ce53-57da-abc2-7cd69befeb01" or // Rect
            "system_ef0d4cbb-7182-5d93-9bb8-60a5a18f81a5" or // Line
            "system_1da9303d-c362-541a-a77b-244fb0ffcbb6" or // WithCenter
            "system_7dce7f78-2c8f-5a1f-aa80-89dc217dcdff" or // WithoutCenter
            "system_efc67858-0c95-573f-a8a9-d7e07d0a1d55" or // Translate
            "system_77b14581-e34c-5eb1-a820-788618276e46"; // Union

        private static NeoCellPattern Read(object? value, NSGetterEvaluator.Context ctx) =>
            NeoCellPatternStorage.ReadRuntime(value, ctx);

        internal static void Reserve(long count, NSGetterEvaluator.Context ctx)
        {
            if (count < 0 || count > int.MaxValue) throw new NSGetterRuntimeError("CellPattern exceeds the produced collection entry budget.");
            ctx.allocationTracker.ConsumeProducedCollectionEntry((int)count);
        }

        private static int Int(object? value)
        {
            double number = Convert.ToDouble(value);
            if (!double.IsFinite(number) || number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                throw new NSGetterRuntimeError("CellPattern arguments must be int32 integers.");
            return (int)number;
        }

        private static Vector2Int Vector(object? value) =>
            NeoGeneratedTypesSupport.ReadVector2IntValue(value)
                ?? throw new NSGetterRuntimeError("Expected Vector2Int.");

        private static void ValidateRadii(int x, int y)
        {
            if (x < 0 || y < 0) throw new NSGetterRuntimeError("CellPattern radii must be nonnegative.");
        }
    }
}
