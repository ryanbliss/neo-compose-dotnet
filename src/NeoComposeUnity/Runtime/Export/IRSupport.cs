// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;

namespace NeoCompose.Export
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
    }

    public static class InstructionKind
    {
        public const string Variable = "variable";
        public const string If = "if";
        public const string Return = "return";
        public const string Throw = "throw";
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
        public const string Select = "select";
        public const string First = "first";
        public const string FirstOrDefault = "firstOrDefault";
        public const string Where = "where";
        public const string Contains = "contains";
        public const string Count = "count";
    }

    // -----------------------------------------------------------------------
    // Small POCOs supporting the IR. These mirror TS-side helper interfaces
    // that aren't themselves discriminated unions — they're concrete shapes
    // referenced by Pointer / Instruction / Operation / Function variants.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Variable bound into a function's scope. Mirrors TS-side
    /// <c>INSVariable</c>.
    /// </summary>
    public class Variable
    {
        public string id;
        public TypeInfo typeInfo;
        public Pointer pointer;
    }

    /// <summary>
    /// Literal value expression. Mirrors TS-side <c>INSValue</c>. The
    /// inner <see cref="value"/> is the polymorphic primitive payload
    /// (bool / int / float / string / null) — surfaced as
    /// <see cref="JToken"/> so callers dispatch on
    /// <see cref="typeInfo"/>.
    /// </summary>
    public class Value
    {
        public TypeInfo typeInfo;
        public JToken value;
    }

    /// <summary>
    /// <c>pointer[key]</c> indexing — mirrors TS-side <c>INSKeyOf</c>.
    /// </summary>
    public class KeyOf
    {
        public Pointer pointer;
        public Pointer key;
    }

    /// <summary>
    /// Per-entry pair for the <c>dictLiteral</c> Pointer variant.
    /// Mirrors the inline TS shape <c>{ key, value }[]</c> on
    /// <c>INSPointerDictLiteral.entries</c>.
    /// </summary>
    public class DictLiteralPair
    {
        public Pointer key;
        public Pointer value;
    }

    /// <summary>
    /// Arithmetic operation info. Mirrors TS-side
    /// <c>INSArithmeticOperationInfo</c>.
    /// </summary>
    public class ArithmeticOpInfo
    {
        /// <summary>One of <see cref="ArithmeticOpKind"/>.</summary>
        public string type;
        public Pointer[] pointers;
    }

    /// <summary>
    /// Comparison condition. Mirrors TS-side <c>INSCondition</c>.
    /// </summary>
    public class Condition
    {
        /// <summary>One of <see cref="OperatorKind"/>.</summary>
        public string type;
        public Pointer operand1;
        public Pointer operand2;
    }

    /// <summary>
    /// Logical link onto another boolean expression. Mirrors TS-side
    /// <c>INSLogicalConnective</c>.
    /// </summary>
    public class LogicalConnective
    {
        /// <summary>One of <see cref="LogicalOpKind"/>.</summary>
        public string type;
        public BooleanExpression to;
    }

    /// <summary>
    /// Top-level boolean expression — a single condition plus an
    /// optional linked-list tail. Mirrors TS-side
    /// <c>INSBooleanExpression</c>.
    /// </summary>
    public class BooleanExpression
    {
        public Condition condition;
        public LogicalConnective connective;
    }

    /// <summary>
    /// One branch of an <c>if</c> instruction. Mirrors TS-side
    /// <c>INSConditionalBranch</c>.
    /// </summary>
    public class ConditionalBranch
    {
        public BooleanExpression expression;
        public Instruction[] instructions;
    }

    /// <summary>
    /// Function with a typed return — used both as the top-level
    /// <c>TNSGetter</c> shape and as the lambda body inside collection
    /// functions (Where/Select/etc.). Mirrors TS-side
    /// <c>INSFunctionWithReturnType</c>.
    /// </summary>
    public class FunctionWithReturnType
    {
        public Variable[] parameters;
        public Instruction[] instructions;
        public TypeInfo typeInfo;
    }
}
