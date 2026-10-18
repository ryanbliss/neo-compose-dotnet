// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.Json
{
    /// <summary>Typed vector intrinsic arguments in source evaluation order.</summary>
    public sealed class FunctionVectorOpInfo
    {
        public string kind = null!;
        public string op = null!;
        public Pointer[] argPointers = null!;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public bool? optional;

        internal string? ValidationError()
        {
            bool integer = kind is "Vector2Int" or "Vector3Int";
            bool vector = integer || kind is "Vector2" or "Vector3";
            if (!vector && kind != "Color")
                return $"vectorOp has unknown kind '{kind}'.";
            int arity = op switch
            {
                "negate" or "magnitude" or "sqrMagnitude" or "normalized" => 1,
                "add" or "subtract" or "multiply" or "scaleLeft" or "divide" or "dot" or "cross" or "distance" or "min" or "max" => 2,
                "lerp" or "moveTowards" => 3,
                _ => 0,
            };
            if (arity == 0)
                return $"vectorOp has unknown operation '{op}'.";
            if ((kind == "Color" && op != "lerp")
                || (integer && op is "normalized" or "dot" or "cross" or "lerp" or "moveTowards")
                || (op == "cross" && kind != "Vector3"))
                return $"vectorOp operation '{op}' is not supported for {kind}.";
            if (optional.HasValue && (optional != true || op is not ("magnitude" or "sqrMagnitude" or "normalized")))
                return "vectorOp optional is only supported for derived properties.";
            if (argPointers is null || argPointers.Length != arity)
                return $"vectorOp {kind}.{op} requires {arity} arguments.";
            foreach (Pointer pointer in argPointers)
            {
                if (pointer is null)
                    return "vectorOp arguments must be pointers.";
            }
            return null;
        }
    }
}
