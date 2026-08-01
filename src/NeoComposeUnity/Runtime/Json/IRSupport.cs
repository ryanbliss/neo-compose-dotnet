// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    // -----------------------------------------------------------------------
    // Discriminator string constants.
    // The TS side uses string-valued enums (NSPointerType, NSInstructionType,
    // etc.) whose JSON values are the lowercase variant names. C# stores the
    // discriminator as `string` and exposes these constants for comparison.
    // -----------------------------------------------------------------------

    public static class PointerKind
    {
        public const string Reference = "reference";
        public const string Variable = "variable";
        public const string Value = "value";
        public const string Operation = "operation";
        public const string Function = "function";
        public const string KeyOf = "keyOf";
        public const string ListLiteral = "listLiteral";
        public const string DictLiteral = "dictLiteral";
        public const string ForceUnwrap = "forceUnwrap";
        public const string IsCheck = "isCheck";
        public const string CallGetter = "callGetter";
        public const string Coalesce = "coalesce";
        public const string ToBool = "toBool";
        public const string Stringify = "stringify";
        public const string CallFunction = "callFunction";
        public const string FunctionErrorCheck = "functionErrorCheck";
        public const string StaticMember = "staticMember";
    }

    public static class InstructionKind
    {
        public const string Variable = "variable";
        public const string If = "if";
        public const string Return = "return";
        public const string Throw = "throw";
        public const string Assign = "assign";
        public const string CollectionCall = "collectionCall";
        public const string FunctionCall = "functionCall";
        public const string For = "for";
        public const string ForEach = "forEach";
        public const string Break = "break";
        public const string Continue = "continue";
        public const string Switch = "switch";
        public const string Try = "try";
    }

    public static class OperationKind
    {
        public const string Arithmetic = "arithmetic";
        public const string Boolean = "boolean";
    }

    public static class ArithmeticOpKind
    {
        public const string Addition = "+";
        public const string Subtraction = "-";
        public const string Division = "/";
        public const string Multiplication = "*";
        public const string Remainder = "%";
    }

    public static class OperatorKind
    {
        public const string GreaterThan = "greaterThan";
        public const string GreaterThanOrEqualTo = "greaterThanOrEqualTo";
        public const string LessThan = "lessThan";
        public const string LessThanOrEqualTo = "lessThanOrEqualTo";
        public const string EqualTo = "equalTo";
        public const string DoesNotEqual = "doesNotEqual";
    }

    public static class LogicalOpKind
    {
        public const string And = "&&";
        public const string Or = "||";
    }

    public static class FunctionKind
    {
        public const string ClassClone = "classClone";
        public const string ClassConstructor = "classConstructor";
        /// <summary>
        /// P43 §6.1 — <c>new Foo(Named: …)</c> against a class that declares
        /// constructors. Distinct from <see cref="ClassConstructor"/>, which
        /// stays the schema-derived positional form used by classes that
        /// declare none.
        /// </summary>
        public const string DeclaredConstructor = "declaredConstructor";
        public const string Select = "select";
        public const string First = "first";
        public const string FirstOrDefault = "firstOrDefault";
        public const string Where = "where";
        public const string Contains = "contains";
        public const string Count = "count";
        public const string VisitCount = "visitCount";
        public const string HasVisited = "hasVisited";
        public const string VectorConstructor = "vectorConstructor";
        /// <summary>P42 §2.3 — <c>Images.&lt;Name&gt;.Slice(n)</c>.</summary>
        public const string ImageSlice = "imageSlice";
        public const string StringOp = "stringOp";
        public const string DecimalOp = "decimalOp";
        public const string ListIndex = "listIndex";
    }

    public static class ListIndexKeyKind
    {
        public const string String = "string";
        public const string Enum = "enum";
    }

    /// <summary>
    /// Wire values of the TS-side <c>TNSStringOp</c> union — the ops a
    /// <c>stringOp</c> function IR node may carry.
    /// </summary>
    public static class StringOpKind
    {
        public const string ToLower = "toLower";
        public const string ToUpper = "toUpper";
        public const string Trim = "trim";
        public const string StartsWith = "startsWith";
        public const string EndsWith = "endsWith";
    }

    /// <summary>
    /// Wire values of the TS-side <c>TNSDecimalOp</c> union — the ops a
    /// <c>decimalOp</c> function IR node may carry
    /// (specs/decimal-member.md decision 7).
    /// </summary>
    public static class DecimalOpKind
    {
        public const string Round = "round";
        public const string Divide = "divide";
        public const string ToFloat = "toFloat";
        public const string ToDecimal = "toDecimal";
    }

    public static class WritabilityKind
    {
        public const string Local = "local";
        public const string Save = "save";
        public const string Session = "session";
        public const string Immutable = "immutable";
        public const string ImmutableToSaveLookup = "immutableToSaveLookup";
        public const string ImmutableToSessionLookup = "immutableToSessionLookup";
        public const string Runtime = "runtime";
        public const string ReadOnly = "readOnly";
        public const string Setter = "setter";
    }

    public static class CollectionMutationKind
    {
        public const string Add = "Add";
        public const string Remove = "Remove";
        public const string RemoveAt = "RemoveAt";
        public const string Clear = "Clear";
    }

    public static class FunctionErrorCheckKind
    {
        public const string Throws = "throws";
        public const string DoesNotThrow = "doesNotThrow";
    }

    // -----------------------------------------------------------------------
    // Small POCOs supporting the IR. These mirror TS-side helper interfaces
    // that aren't themselves discriminated unions — they're concrete shapes
    // referenced by Pointer / Instruction / Operation / Function variants.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Variable bound into a function's scope. Mirrors TS-side
    /// <c>INSVariable</c>. All fields required.
    /// </summary>
    public class Variable
    {
        public string id = null!;
        public TypeInfo typeInfo = null!;
        public Pointer pointer = null!;
    }

    /// <summary>
    /// Literal value expression. Mirrors TS-side <c>INSValue</c>. The
    /// inner <see cref="value"/> is the polymorphic primitive payload
    /// (bool / int / float / string / null) — surfaced as
    /// <see cref="JToken"/> so callers dispatch on
    /// <see cref="typeInfo"/>. The TS-side <c>value: TValue</c> is
    /// required, but the wire payload may legitimately be JSON
    /// <c>null</c> (for Null-typed values), so the JToken is nullable.
    /// </summary>
    public class Value
    {
        public TypeInfo typeInfo = null!;
        public JToken? value;
    }

    /// <summary>
    /// <c>pointer[key]</c> indexing — mirrors TS-side <c>INSKeyOf</c>.
    /// </summary>
    public class KeyOf
    {
        public Pointer pointer = null!;
        public Pointer key = null!;
    }

    /// <summary>
    /// Per-entry pair for the <c>dictLiteral</c> Pointer variant.
    /// Mirrors the inline TS shape <c>{ key, value }[]</c> on
    /// <c>INSPointerDictLiteral.entries</c>.
    /// </summary>
    public class DictLiteralPair
    {
        public Pointer key = null!;
        public Pointer value = null!;
    }

    /// <summary>
    /// Arithmetic operation info. Mirrors TS-side
    /// <c>INSArithmeticOperationInfo</c>.
    /// </summary>
    public class ArithmeticOpInfo
    {
        /// <summary>One of <see cref="ArithmeticOpKind"/>.</summary>
        public string type = null!;
        public Pointer[] pointers = null!;
        /// <summary>
        /// TS-side <c>decimal?: boolean</c> — when true, this operation is
        /// Decimal-typed and evaluates over canonical decimal strings via
        /// <see cref="NeoDecimalMath"/> (specs/decimal-member.md
        /// decision 7). Only <c>+</c>/<c>-</c>/<c>*</c> ever carry the
        /// stamp (the compiler rejects <c>/</c> and <c>%</c> on decimals).
        /// The TS-side name <c>decimal</c> is a C# keyword —
        /// <see cref="JsonPropertyAttribute"/> keeps the wire form
        /// unchanged (the <c>else</c>/<c>elseInstructions</c> precedent).
        /// </summary>
        [JsonProperty("decimal")]
        public bool? isDecimal;
    }

    /// <summary>
    /// Comparison condition. Mirrors TS-side <c>INSCondition</c>.
    /// </summary>
    public class Condition
    {
        /// <summary>One of <see cref="OperatorKind"/>.</summary>
        public string type = null!;
        public Pointer operand1 = null!;
        public Pointer operand2 = null!;
        /// <summary>
        /// TS-side <c>decimal?: boolean</c> — when true, this comparison is
        /// exact decimal: operands are canonical decimal strings compared
        /// scale-blind via <see cref="NeoDecimalMath.Compare"/>
        /// ("1.10" == "1.1" is true). Wire name preserved per the
        /// <c>else</c>/<c>elseInstructions</c> precedent.
        /// </summary>
        [JsonProperty("decimal")]
        public bool? isDecimal;
    }

    /// <summary>
    /// Logical link onto another boolean expression. Mirrors TS-side
    /// <c>INSLogicalConnective</c>.
    /// </summary>
    public class LogicalConnective
    {
        /// <summary>One of <see cref="LogicalOpKind"/>.</summary>
        public string type = null!;
        public BooleanExpression to = null!;
    }

    /// <summary>
    /// Top-level boolean expression — a single condition plus an
    /// optional linked-list tail. Mirrors TS-side
    /// <c>INSBooleanExpression</c>. <see cref="connective"/> is
    /// <c>connective?: INSLogicalConnective | null</c> on the wire —
    /// nullable here.
    /// </summary>
    public class BooleanExpression
    {
        public Condition condition = null!;
        public LogicalConnective? connective;
    }

    /// <summary>
    /// One branch of an <c>if</c> instruction. Mirrors TS-side
    /// <c>INSConditionalBranch</c>.
    /// </summary>
    public class ConditionalBranch
    {
        public BooleanExpression expression = null!;
        public Instruction[] instructions = null!;
    }

    /// <summary>
    /// Assignment / collection-call target. Mirrors TS-side
    /// <c>INSWriteTarget</c>.
    /// </summary>
    public class WriteTarget
    {
        public Pointer pointer = null!;
        public TypeInfo typeInfo = null!;
        /// <summary>One of <see cref="WritabilityKind"/>.</summary>
        public string? writability;
    }

    /// <summary>
    /// Function with a typed return — used both as the top-level
    /// <c>TNSGetter</c> shape and as the lambda body inside collection
    /// functions (Where/Select/etc.). Mirrors TS-side
    /// <c>INSFunctionWithReturnType</c>.
    /// </summary>
    public class FunctionWithReturnType
    {
        /// <summary>
        /// Latest NeoScript compiler revision understood by this runtime.
        /// Revision 1 is the legacy unstamped wire shape; revision 2 pins
        /// declaration member ids on member pointers; revision 3 adds the
        /// P43 <c>declaredConstructor</c> function kind; revision 4 adds
        /// P50 loop and loop-control instructions; revision 5 adds the P51
        /// <c>switch</c> instruction; revision 6 adds the P52
        /// <c>try</c> instruction.
        /// </summary>
        public const int CurrentCompilerRevision = 6;

        /// <summary>
        /// Optional for backward compatibility. Absence means legacy
        /// compiler revision 1.
        /// </summary>
        public int? compilerRevision;
        public Variable[] parameters = null!;
        public Instruction[] instructions = null!;
        public TypeInfo typeInfo = null!;
    }
}
