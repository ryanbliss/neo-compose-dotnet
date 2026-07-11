// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSFunction</c> discriminated
    /// union. Six variants (Select / First / FirstOrDefault / Where /
    /// Contains / Count). Newtonsoft dispatches on <see cref="type"/>
    /// via {@link FunctionConverter}.
    ///
    /// Each variant carries a per-shape <c>info</c> sub-object — see
    /// the per-subclass docs.
    /// </summary>
    [JsonConverter(typeof(FunctionConverter))]
    public abstract class Function
    {
        /// <summary>One of <see cref="FunctionKind"/>.</summary>
        public string type = null!;
    }

    // ---------- Per-info shapes ----------
    // Mirrors TS-side INSFunctionCollection*Info family.

    /// <summary>
    /// Info for the intrinsic Custom.Clone operation. The exact constructed
    /// Custom type is carried on the wire so this never masquerades as a
    /// schema Function attribute.
    /// </summary>
    public class FunctionCustomCloneInfo
    {
        public Pointer receiverPointer = null!;
        public CustomTypeInfo customTypeInfo = null!;
    }

    /// <summary>
    /// Info shape for <c>select</c>: collection + projection function.
    /// Mirrors TS-side <c>INSFunctionCollectionSelectInfo</c>.
    /// </summary>
    public class FunctionCollectionSelectInfo
    {
        public Pointer collectionPointer = null!;
        public FunctionWithReturnType function = null!;
    }

    /// <summary>
    /// Info shape for <c>where</c>: collection + required Bool predicate.
    /// Mirrors TS-side <c>INSFunctionCollectionBoolInfo</c>.
    /// </summary>
    public class FunctionCollectionBoolInfo
    {
        public Pointer collectionPointer = null!;
        public FunctionWithReturnType function = null!;
    }

    /// <summary>
    /// Info shape for <c>first</c> / <c>firstOrDefault</c>: collection +
    /// optional Bool predicate. Mirrors TS-side
    /// <c>INSFunctionCollectionOptionalBoolInfo</c>.
    /// <see cref="function"/> is <c>... | null | undefined</c> on the
    /// wire — nullable here; absent / null both mean "no predicate".
    /// </summary>
    public class FunctionCollectionOptionalBoolInfo
    {
        public Pointer collectionPointer = null!;
        public FunctionWithReturnType? function;
    }

    /// <summary>
    /// Info shape for <c>contains</c>: collection + value pointer to
    /// compare against each entry. Mirrors TS-side
    /// <c>INSFunctionCollectionContainsInfo</c>.
    /// </summary>
    public class FunctionCollectionContainsInfo
    {
        public Pointer collectionPointer = null!;
        public Pointer valuePointer = null!;
    }

    /// <summary>
    /// Info shape for <c>count</c>: collection only. Mirrors TS-side
    /// <c>INSFunctionCollectionInfo</c>.
    /// </summary>
    public class FunctionCollectionInfo
    {
        public Pointer collectionPointer = null!;
    }

    /// <summary>
    /// Info shape for global dialogue-memory functions
    /// (<c>VisitCount</c> / <c>HasVisited</c>): one string pointer.
    /// Mirrors TS-side <c>INSFunctionDialogueMemoryInfo</c>.
    /// </summary>
    public class FunctionDialogueMemoryInfo
    {
        public Pointer pointer = null!;
    }

    /// <summary>
    /// Info shape for global vector constructors. Mirrors TS-side
    /// <c>INSFunctionVectorConstructorInfo</c>.
    /// </summary>
    public class FunctionVectorConstructorInfo
    {
        public AttributeType vectorType;
        public Pointer[] componentPointers = null!;
    }

    /// <summary>
    /// Info shape for <c>stringOp</c>: a method-call-style string builtin
    /// (<c>ToLower</c>/<c>ToUpper</c>/<c>Trim</c>/<c>StartsWith</c>/
    /// <c>EndsWith</c>). Mirrors TS-side <c>INSFunctionStringOpInfo</c>.
    /// </summary>
    public class FunctionStringOpInfo
    {
        /// <summary>One of <see cref="StringOpKind"/>.</summary>
        public string op = null!;
        public Pointer receiverPointer = null!;
        /// <summary>Present for startsWith/endsWith.</summary>
        public Pointer? argPointer;
    }

    /// <summary>
    /// Info shape for <c>decimalOp</c>: a method-call-style decimal builtin
    /// (specs/decimal-attribute.md decision 7). Mirrors TS-side
    /// <c>INSFunctionDecimalOpInfo</c>.
    /// </summary>
    public class FunctionDecimalOpInfo
    {
        /// <summary>One of <see cref="DecimalOpKind"/>.</summary>
        public string op = null!;
        /// <summary>Decimal receiver (round/divide/toFloat) or Float receiver (toDecimal).</summary>
        public Pointer receiverPointer = null!;
        /// <summary>Present for divide: the divisor (Decimal, or Int widened exactly).</summary>
        public Pointer? argPointer;
        /// <summary>
        /// Present for round/divide/toDecimal: the fractional-digit count
        /// (Int; runtime-validated to 0..28 with a distinct error).
        /// </summary>
        public Pointer? digitsPointer;
    }

    // ---------- Per-function variants ----------

    public class CustomCloneFunction : Function
    {
        public FunctionCustomCloneInfo info = null!;
    }

    public class SelectFunction : Function
    {
        public FunctionCollectionSelectInfo info = null!;
    }

    public class FirstFunction : Function
    {
        public FunctionCollectionOptionalBoolInfo info = null!;
    }

    public class FirstOrDefaultFunction : Function
    {
        public FunctionCollectionOptionalBoolInfo info = null!;
    }

    public class WhereFunction : Function
    {
        public FunctionCollectionBoolInfo info = null!;
    }

    public class ContainsFunction : Function
    {
        public FunctionCollectionContainsInfo info = null!;
    }

    public class CountFunction : Function
    {
        public FunctionCollectionInfo info = null!;
    }

    public class VisitCountFunction : Function
    {
        public FunctionDialogueMemoryInfo info = null!;
    }

    public class HasVisitedFunction : Function
    {
        public FunctionDialogueMemoryInfo info = null!;
    }

    public class VectorConstructorFunction : Function
    {
        public FunctionVectorConstructorInfo info = null!;
    }

    public class StringOpFunction : Function
    {
        public FunctionStringOpInfo info = null!;
    }

    public class DecimalOpFunction : Function
    {
        public FunctionDecimalOpInfo info = null!;
    }

    public class FunctionConverter : DiscriminatedConverter<Function>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case FunctionKind.CustomClone: return typeof(CustomCloneFunction);
                case FunctionKind.Select: return typeof(SelectFunction);
                case FunctionKind.First: return typeof(FirstFunction);
                case FunctionKind.FirstOrDefault: return typeof(FirstOrDefaultFunction);
                case FunctionKind.Where: return typeof(WhereFunction);
                case FunctionKind.Contains: return typeof(ContainsFunction);
                case FunctionKind.Count: return typeof(CountFunction);
                case FunctionKind.VisitCount: return typeof(VisitCountFunction);
                case FunctionKind.HasVisited: return typeof(HasVisitedFunction);
                case FunctionKind.VectorConstructor: return typeof(VectorConstructorFunction);
                case FunctionKind.StringOp: return typeof(StringOpFunction);
                case FunctionKind.DecimalOp: return typeof(DecimalOpFunction);
                default: return null;
            }
        }
    }
}
