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
    /// P43 §6.1 — one named argument of a declared-constructor call. Unlike
    /// <see cref="FunctionClassConstructorField"/> this names a <b>parameter</b>
    /// of the resolved overload, which is not a schema key and not a member.
    /// </summary>
    public class DeclaredConstructorArgument
    {
        /// <summary>Declared parameter name on the resolved overload.</summary>
        public string name = null!;
        public Pointer valuePointer = null!;
    }

    /// <summary>
    /// P43 §6.1 payload for <c>new Foo(Named: …) { X = … }</c> against a class
    /// that declares constructors.
    /// </summary>
    public class DeclaredConstructorInfo
    {
        public ClassTypeInfo schemaClassInfo = null!;

        /// <summary>
        /// Resolved overload's constructor record id. <c>null</c> is the
        /// implicit <c>new()</c> a class keeps even after declaring
        /// constructors (§6.1.2): member initializers only, no body.
        /// </summary>
        public string? constructorId;

        public DeclaredConstructorArgument[] args = null!;

        /// <summary>
        /// Call-site initializer block, applied <b>last</b> so an explicit
        /// assignment wins over anything the body wrote (§6.1 step 4). Same
        /// shape as the <c>classConstructor</c> fields.
        /// </summary>
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

    /// <summary>
    /// Info shape for <c>mathOp</c>: a numeric builtin on the <c>Math</c>
    /// namespace (<c>Min</c>/<c>Max</c>/<c>Clamp</c>/<c>Round</c>/
    /// <c>Floor</c>/<c>Ceiling</c>/<c>Truncate</c>/<c>Abs</c>/<c>Sign</c>/
    /// <c>Sqrt</c> — P69 §4). Mirrors TS-side
    /// <c>INSFunctionMathOpInfo</c>. Static-call shape — argument pointers
    /// only, no receiver — like
    /// <see cref="FunctionVectorConstructorInfo"/> rather than the
    /// receiver-style <see cref="FunctionStringOpInfo"/> /
    /// <see cref="FunctionDecimalOpInfo"/>.
    /// </summary>
    public class FunctionMathOpInfo
    {
        /// <summary>One of <see cref="MathOpKind"/>.</summary>
        public string op = null!;
        /// <summary>
        /// One pointer per declared argument, in source order (1 for the
        /// unary ops, 2 for min/max, 3 for clamp).
        /// </summary>
        public Pointer[] argPointers = null!;
        /// <summary>
        /// TS-side <c>decimal?: true</c> — when true, the arguments are
        /// canonical decimal strings and the op runs on the exact decimal
        /// core (<see cref="NeoDecimalMath"/>), the same stamp arithmetic
        /// operations carry; absence means the IEEE double path. The TS-side
        /// name <c>decimal</c> is a C# keyword —
        /// <see cref="JsonPropertyAttribute"/> keeps the wire form unchanged
        /// (the <c>else</c>/<c>elseInstructions</c> precedent).
        /// </summary>
        [JsonProperty("decimal")]
        public bool? isDecimal;
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

    /// <summary>P67 §4.1 — mirror of <c>INSFunctionVariantInitializeInfo</c>.</summary>
    public class FunctionVariantInitializeInfo
    {
        public Pointer variantPointer = null!;
        public Pointer? rowPointer;
        public ClassTypeInfo schemaClassInfo = null!;
    }

    /// <summary>P67 §4.2 — mirror of <c>INSFunctionVariantApplyInfo</c>.</summary>
    public class FunctionVariantApplyInfo
    {
        public Pointer receiverPointer = null!;
        public Pointer variantPointer = null!;
        public Pointer? rowPointer;
        /// <summary>The RECEIVER's class, which may be a subclass of the variant's (§4.3).</summary>
        public ClassTypeInfo schemaClassInfo = null!;
    }

    public class VariantInitializeFunction : Function
    {
        public FunctionVariantInitializeInfo info = null!;
    }

    public class VariantApplyFunction : Function
    {
        public FunctionVariantApplyInfo info = null!;
    }

    public class DeclaredConstructorFunction : Function
    {
        public DeclaredConstructorInfo info = null!;
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

    public class MathOpFunction : Function
    {
        public FunctionMathOpInfo info = null!;
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
                case FunctionKind.DeclaredConstructor: return typeof(DeclaredConstructorFunction);
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
                case FunctionKind.MathOp: return typeof(MathOpFunction);
                case FunctionKind.ListIndex: return typeof(ListIndexFunction);
                case FunctionKind.VariantInitialize: return typeof(VariantInitializeFunction);
                case FunctionKind.VariantApply: return typeof(VariantApplyFunction);
                default: return null;
            }
        }
    }
}
