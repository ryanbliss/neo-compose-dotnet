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
    /// Info for the intrinsic Class.Clone operation. The exact constructed
    /// Class is carried on the wire so this never masquerades as a
    /// schema Function member.
    /// </summary>
    public class FunctionClassCloneInfo
    {
        public Pointer receiverPointer = null!;
        public ClassTypeInfo schemaClassInfo = null!;
    }

    /// <summary>One explicitly supplied field of a Class constructor.</summary>
    public class FunctionClassConstructorField
    {
        public string schemaKey = null!;
        public string memberId = null!;
        public Pointer valuePointer = null!;
    }

    /// <summary>
    /// Constructor intrinsic payload. Argument ordering/default decisions are
    /// already compiled into <see cref="fields"/> by the shared language
    /// package; the runtime materializes only those explicit values and lets
    /// normal Class defaults fill required omissions.
    /// </summary>
    public class FunctionClassConstructorInfo
    {
        public ClassTypeInfo schemaClassInfo = null!;
        public FunctionClassConstructorField[] fields = null!;
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
    /// Info for a declared List index lookup. The schema provenance is
    /// carried in IR so runtime evaluation does not depend on generated
    /// wrapper classes.
    /// </summary>
    public class FunctionListIndexInfo
    {
        public Pointer collectionPointer = null!;
        public string listMemberId = null!;
        public string schemaKey = null!;
        public bool unique;
        /// <summary>One of <see cref="ListIndexKeyKind"/>.</summary>
        public string keyKind = null!;
        public string? keyEnumId;
        /// <summary>
        /// Present for direct keyed lookup; omitted when the declared index
        /// is evaluated as a read-only Dictionary view.
        /// </summary>
        public Pointer? keyPointer;
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
        public MemberKind vectorType;
        public Pointer[] componentPointers = null!;
    }

    /// <summary>
    /// P42 §2.3. Info shape for <c>imageSlice</c> —
    /// <c>Images.&lt;Name&gt;.Slice(n)</c>. Mirrors TS-side
    /// <c>INSFunctionImageSliceInfo</c>. Modelled on
    /// <see cref="FunctionVectorConstructorInfo"/>: both build a raw
    /// structured-leaf value out of pointers, with no schema provenance.
    /// </summary>
    public class FunctionImageSliceInfo
    {
        /// <summary>
        /// Pointer to the project image <b>file record id</b>. The compiler
        /// already resolved the registry symbol against the project document,
        /// so this is a plain string on both runtimes.
        /// </summary>
        public Pointer filePointer = null!;

        /// <summary>Pointer to the int slice index.</summary>
        public Pointer sliceIndexPointer = null!;
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
    /// (specs/decimal-member.md decision 7). Mirrors TS-side
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

    public class ClassCloneFunction : Function
    {
        public FunctionClassCloneInfo info = null!;
    }

    public class ClassConstructorFunction : Function
    {
        public FunctionClassConstructorInfo info = null!;
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

    public class ImageSliceFunction : Function
    {
        public FunctionImageSliceInfo info = null!;
    }

    public class StringOpFunction : Function
    {
        public FunctionStringOpInfo info = null!;
    }

    public class DecimalOpFunction : Function
    {
        public FunctionDecimalOpInfo info = null!;
    }

    public class ListIndexFunction : Function
    {
        public FunctionListIndexInfo info = null!;
    }

    public class FunctionConverter : DiscriminatedConverter<Function>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case FunctionKind.ClassClone: return typeof(ClassCloneFunction);
                case FunctionKind.ClassConstructor: return typeof(ClassConstructorFunction);
                case FunctionKind.Select: return typeof(SelectFunction);
                case FunctionKind.First: return typeof(FirstFunction);
                case FunctionKind.FirstOrDefault: return typeof(FirstOrDefaultFunction);
                case FunctionKind.Where: return typeof(WhereFunction);
                case FunctionKind.Contains: return typeof(ContainsFunction);
                case FunctionKind.Count: return typeof(CountFunction);
                case FunctionKind.VisitCount: return typeof(VisitCountFunction);
                case FunctionKind.HasVisited: return typeof(HasVisitedFunction);
                case FunctionKind.VectorConstructor: return typeof(VectorConstructorFunction);
                case FunctionKind.ImageSlice: return typeof(ImageSliceFunction);
                case FunctionKind.StringOp: return typeof(StringOpFunction);
                case FunctionKind.DecimalOp: return typeof(DecimalOpFunction);
                case FunctionKind.ListIndex: return typeof(ListIndexFunction);
                default: return null;
            }
        }
    }
}
