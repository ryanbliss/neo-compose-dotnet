// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSInstruction</c>
    /// discriminated union. Four variants:
    /// <see cref="VariableInstruction"/>, <see cref="IfInstruction"/>,
    /// <see cref="ReturnInstruction"/>, <see cref="ThrowInstruction"/>.
    /// Newtonsoft dispatches on the string <see cref="type"/> via
    /// {@link InstructionConverter}.
    /// </summary>
    [JsonConverter(typeof(InstructionConverter))]
    public abstract class Instruction
    {
        /// <summary>One of <see cref="InstructionKind"/>.</summary>
        public string type = null!;
    }

    /// <summary>
    /// Local variable declaration. Mirrors TS-side
    /// <c>INSInstructionVariable</c>.
    /// </summary>
    public class VariableInstruction : Instruction
    {
        public Variable variable = null!;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionIfBranch</c>. The TS-side field
    /// <c>else</c> is a C# reserved word; <see cref="JsonPropertyAttribute"/>
    /// keeps the wire form unchanged while exposing it under
    /// <see cref="elseInstructions"/> on the C# side. The TS field is
    /// <c>else?: TNSInstructions | null</c> — nullable here.
    /// </summary>
    public class IfInstruction : Instruction
    {
        public ConditionalBranch[] branches = null!;

        [JsonProperty("else")]
        public Instruction[]? elseInstructions;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionReturn</c>. The TS-side
    /// <c>pointer</c> field is <c>TNSPointer | null</c> — <c>null</c>
    /// represents a bare <c>return;</c>. Newtonsoft passes JSON
    /// <c>null</c> through to a C# <c>null</c> reference, so callers
    /// can distinguish "bare return" from "return X" by checking
    /// whether <see cref="pointer"/> is null.
    /// </summary>
    public class ReturnInstruction : Instruction
    {
        public Pointer? pointer;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionThrow</c>. The TS-side
    /// <c>pointer</c> is the message to surface as the cell's error
    /// string at runtime — required, never null.
    /// </summary>
    public class ThrowInstruction : Instruction
    {
        public Pointer pointer = null!;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionAssign</c>. The TS-side
    /// <c>operator</c> field is a C# keyword; expose it as
    /// <see cref="operatorValue"/> while keeping the wire name stable.
    /// </summary>
    public class AssignInstruction : Instruction
    {
        public WriteTarget target = null!;
        [JsonProperty("operator")]
        public string operatorValue = null!;
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSInstructionCollectionCall</c>.</summary>
    public class CollectionCallInstruction : Instruction
    {
        public WriteTarget target = null!;
        /// <summary>One of <see cref="CollectionMutationKind"/>.</summary>
        public string mutation = null!;
        public Pointer[] args = null!;
    }

    public class FunctionCallInstruction : Instruction
    {
        public CallFunctionPointer call = null!;
    }

    /// <summary>Loop-local binding metadata shared by loop instructions.</summary>
    public class LoopBinding
    {
        public string id = null!;
        public TypeInfo typeInfo = null!;
        [JsonProperty("readonly")]
        public bool isReadonly;
        public string? writability;
    }

    /// <summary>Mirror of the P50 <c>for</c> instruction.</summary>
    public class ForInstruction : Instruction
    {
        public Variable initializer = null!;
        public BooleanExpression condition = null!;
        public AssignInstruction iterator = null!;
        public Instruction[] instructions = null!;
    }

    /// <summary>Mirror of the P50 <c>forEach</c> instruction.</summary>
    public class ForEachInstruction : Instruction
    {
        public LoopBinding binding = null!;
        public Pointer collectionPointer = null!;
        public TypeInfo collectionTypeInfo = null!;
        public Instruction[] instructions = null!;
    }

    /// <summary>Exits the nearest enclosing loop or switch.</summary>
    public class BreakInstruction : Instruction { }

    /// <summary>Advances the nearest enclosing loop.</summary>
    public class ContinueInstruction : Instruction { }

    /// <summary>
    /// One normalized case section in the P51 <c>switch</c> instruction.
    /// Adjacent source labels share one section and one instruction body.
    /// </summary>
    public class SwitchSection
    {
        public Value[] labels = null!;
        public Instruction[] instructions = null!;
    }

    /// <summary>Mirror of the P51 <c>switch</c> instruction.</summary>
    public class SwitchInstruction : Instruction
    {
        public Pointer selector = null!;
        public TypeInfo selectorTypeInfo = null!;
        public SwitchSection[] sections = null!;
        public Instruction[]? defaultInstructions;
    }

    public class InstructionConverter : DiscriminatedConverter<Instruction>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case InstructionKind.Variable: return typeof(VariableInstruction);
                case InstructionKind.If: return typeof(IfInstruction);
                case InstructionKind.Return: return typeof(ReturnInstruction);
                case InstructionKind.Throw: return typeof(ThrowInstruction);
                case InstructionKind.Assign: return typeof(AssignInstruction);
                case InstructionKind.CollectionCall: return typeof(CollectionCallInstruction);
                case InstructionKind.FunctionCall: return typeof(FunctionCallInstruction);
                case InstructionKind.For: return typeof(ForInstruction);
                case InstructionKind.ForEach: return typeof(ForEachInstruction);
                case InstructionKind.Break: return typeof(BreakInstruction);
                case InstructionKind.Continue: return typeof(ContinueInstruction);
                case InstructionKind.Switch: return typeof(SwitchInstruction);
                default: return null;
            }
        }

        protected override void ValidateObject(JObject obj, Type concrete)
        {
            if (concrete == typeof(FunctionCallInstruction)
                && obj["call"]?.Type != JTokenType.Object)
            {
                throw new JsonSerializationException(
                    "FunctionCallInstruction must contain a 'call' object.");
            }
            if (concrete == typeof(ForInstruction)
                && !IsValidForInstruction(obj))
            {
                throw new JsonSerializationException(
                    "ForInstruction must contain a valid initializer, condition, assign iterator, and instructions array.");
            }
            if (concrete == typeof(ForEachInstruction)
                && !IsValidForEachInstruction(obj))
            {
                throw new JsonSerializationException(
                    "ForEachInstruction must contain a read-only binding, collection pointer, required collection type, and instructions array.");
            }
            if (concrete == typeof(SwitchInstruction)
                && !IsValidSwitchInstruction(obj))
            {
                throw new JsonSerializationException(
                    "SwitchInstruction must contain a scalar or enum selector type, normalized unique labels, instruction sections, and an optional default instruction array.");
            }
        }

        private static bool IsValidForInstruction(JObject obj)
        {
            if (obj["initializer"] is not JObject initializer
                || !IsNonEmptyString(initializer["id"])
                || initializer["typeInfo"]?.Type != JTokenType.Object
                || !IsPointerObject(initializer["pointer"]))
            {
                return false;
            }
            if (obj["condition"] is not JObject condition
                || !IsValidBooleanExpression(condition))
            {
                return false;
            }
            if (obj["iterator"] is not JObject iterator
                || iterator["type"]?.Value<string>() != InstructionKind.Assign
                || iterator["target"] is not JObject target
                || !IsPointerObject(target["pointer"])
                || target["typeInfo"]?.Type != JTokenType.Object
                || !IsOptionalWritability(target["writability"])
                || !IsAssignmentOperator(iterator["operator"])
                || !IsPointerObject(iterator["pointer"]))
            {
                return false;
            }
            return obj["instructions"]?.Type == JTokenType.Array;
        }

        private static bool IsValidBooleanExpression(JObject expression)
        {
            if (expression["condition"] is not JObject condition
                || !IsComparisonOperator(condition["type"])
                || !IsPointerObject(condition["operand1"])
                || !IsPointerObject(condition["operand2"]))
            {
                return false;
            }
            if (expression["connective"] is null
                || expression["connective"]!.Type == JTokenType.Null)
            {
                return true;
            }
            return expression["connective"] is JObject connective
                && (connective["type"]?.Value<string>() == LogicalOpKind.And
                    || connective["type"]?.Value<string>() == LogicalOpKind.Or)
                && connective["to"] is JObject next
                && IsValidBooleanExpression(next);
        }

        private static bool IsComparisonOperator(JToken token)
        {
            if (token?.Type != JTokenType.String)
                return false;

            switch (token.Value<string>())
            {
                case OperatorKind.GreaterThan:
                case OperatorKind.GreaterThanOrEqualTo:
                case OperatorKind.LessThan:
                case OperatorKind.LessThanOrEqualTo:
                case OperatorKind.EqualTo:
                case OperatorKind.DoesNotEqual:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsValidForEachInstruction(JObject obj)
        {
            if (obj["binding"] is not JObject binding
                || !IsNonEmptyString(binding["id"])
                || binding["typeInfo"]?.Type != JTokenType.Object
                || binding["readonly"]?.Type != JTokenType.Boolean
                || binding["readonly"]!.Value<bool>() != true
                || !IsOptionalWritability(binding["writability"]))
            {
                return false;
            }
            if (!IsPointerObject(obj["collectionPointer"])
                || obj["collectionTypeInfo"] is not JObject collectionType
                || collectionType["required"]?.Type != JTokenType.Boolean
                || collectionType["required"]!.Value<bool>() != true
                || collectionType["entryTypeInfo"]?.Type != JTokenType.Object
                || collectionType["type"]?.Type != JTokenType.Integer)
            {
                return false;
            }

            MemberKind kind = (MemberKind)collectionType["type"]!.Value<int>();
            if (kind != MemberKind.List
                && kind != MemberKind.Dictionary
                && kind != MemberKind.Lookup)
            {
                return false;
            }
            if (kind == MemberKind.Lookup
                && !IsNonEmptyString(collectionType["collectionMemberId"]))
            {
                return false;
            }
            return obj["instructions"]?.Type == JTokenType.Array;
        }

        private static bool IsValidSwitchInstruction(JObject obj)
        {
            if (!IsPointerObject(obj["selector"])
                || obj["selectorTypeInfo"] is not JObject selectorType
                || !TryGetSwitchType(
                    selectorType,
                    out MemberKind selectorKind,
                    out bool selectorRequired,
                    out string? selectorEnumId)
                || obj["sections"] is not JArray sections)
            {
                return false;
            }
            if (obj.TryGetValue("defaultInstructions", out JToken? defaultBody)
                && defaultBody.Type != JTokenType.Null
                && defaultBody.Type != JTokenType.Array)
            {
                return false;
            }

            var normalizedLabels = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            foreach (JToken sectionToken in sections)
            {
                if (sectionToken is not JObject section
                    || section["labels"] is not JArray labels
                    || labels.Count == 0
                    || section["instructions"]?.Type != JTokenType.Array)
                {
                    return false;
                }
                foreach (JToken labelToken in labels)
                {
                    if (labelToken is not JObject label
                        || !TryNormalizeSwitchLabel(
                            label,
                            selectorKind,
                            selectorRequired,
                            selectorEnumId,
                            out string? key)
                        || !normalizedLabels.Add(key!))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TryGetSwitchType(
            JObject typeInfo,
            out MemberKind kind,
            out bool required,
            out string? enumId)
        {
            kind = default;
            required = false;
            enumId = null;
            if (typeInfo["type"]?.Type != JTokenType.Integer
                || typeInfo["required"]?.Type != JTokenType.Boolean)
            {
                return false;
            }
            kind = (MemberKind)typeInfo["type"]!.Value<int>();
            required = typeInfo["required"]!.Value<bool>();
            if (kind != MemberKind.Int
                && kind != MemberKind.String
                && kind != MemberKind.Bool
                && kind != MemberKind.Enum)
            {
                return false;
            }
            if (kind == MemberKind.Enum)
            {
                enumId = typeInfo["enumId"]?.Value<string>();
                return !string.IsNullOrEmpty(enumId);
            }
            return true;
        }

        private static bool TryNormalizeSwitchLabel(
            JObject label,
            MemberKind selectorKind,
            bool selectorRequired,
            string? selectorEnumId,
            out string? key)
        {
            key = null;
            if (label["typeInfo"] is not JObject labelType
                || !label.TryGetValue("value", out JToken? value))
            {
                return false;
            }
            if (value.Type == JTokenType.Null)
            {
                if (selectorRequired
                    || labelType["type"]?.Type != JTokenType.Integer
                    || (MemberKind)labelType["type"]!.Value<int>()
                        != MemberKind.Null
                    || labelType["required"]?.Type != JTokenType.Boolean
                    || labelType["required"]!.Value<bool>() != true)
                {
                    return false;
                }
                key = "null";
                return true;
            }
            if (!TryGetSwitchType(
                    labelType,
                    out MemberKind labelKind,
                    out bool labelRequired,
                    out string? labelEnumId)
                || labelKind != selectorKind
                || !labelRequired
                || (selectorKind == MemberKind.Enum
                    && !string.Equals(
                        selectorEnumId,
                        labelEnumId,
                        StringComparison.Ordinal)))
            {
                return false;
            }
            switch (selectorKind)
            {
                case MemberKind.Int:
                    if (value.Type != JTokenType.Integer
                        && value.Type != JTokenType.Float)
                    {
                        return false;
                    }
                    double integer = value.Value<double>();
                    if (double.IsNaN(integer)
                        || double.IsInfinity(integer)
                        || integer != System.Math.Truncate(integer)
                        || System.Math.Abs(integer) > 9007199254740991d)
                    {
                        return false;
                    }
                    if (integer == 0d) integer = 0d;
                    key = "int:" + integer.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                case MemberKind.String:
                    if (value.Type != JTokenType.String) return false;
                    key = "string:" + value.Value<string>();
                    return true;
                case MemberKind.Bool:
                    if (value.Type != JTokenType.Boolean) return false;
                    key = value.Value<bool>() ? "bool:true" : "bool:false";
                    return true;
                case MemberKind.Enum:
                    if (value is not JArray enumOptions
                        || enumOptions.Count != 1
                        || enumOptions[0]?.Type != JTokenType.String
                        || string.IsNullOrEmpty(enumOptions[0]!.Value<string>()))
                    {
                        return false;
                    }
                    key = "enum:" + selectorEnumId + ":" +
                        enumOptions[0]!.Value<string>();
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPointerObject(JToken? token) =>
            token is JObject pointer && IsNonEmptyString(pointer["type"]);

        private static bool IsNonEmptyString(JToken? token) =>
            token?.Type == JTokenType.String
            && !string.IsNullOrEmpty(token.Value<string>());

        private static bool IsAssignmentOperator(JToken? token)
        {
            if (token?.Type != JTokenType.String) return false;
            return token.Value<string>() switch
            {
                "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "++" or "--" => true,
                _ => false,
            };
        }

        private static bool IsOptionalWritability(JToken? token)
        {
            if (token is null || token.Type == JTokenType.Null) return true;
            if (token.Type != JTokenType.String) return false;
            return token.Value<string>() switch
            {
                WritabilityKind.Local
                    or WritabilityKind.Save
                    or WritabilityKind.Session
                    or WritabilityKind.Immutable
                    or WritabilityKind.ImmutableToSaveLookup
                    or WritabilityKind.ImmutableToSessionLookup
                    or WritabilityKind.Runtime
                    or WritabilityKind.ReadOnly
                    or WritabilityKind.Setter => true,
                _ => false,
            };
        }
    }
}
