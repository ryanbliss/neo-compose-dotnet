// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>Canonical primitive slot rules from primitive-intrinsics.ts. Resolutions are cached by NeoInterfaceDispatch.</summary>
    internal static class NeoPrimitiveIntrinsics
    {
        private static readonly Dictionary<string, string> InterfaceNames = NeoPrimitiveConformance.InterfaceIds.ToDictionary(pair => pair.Value, pair => pair.Key);
        private static readonly HashSet<string> Members = new()
        {
            "Abs",
            "Clamp",
            "ClampNative",
            "CopySign",
            "Max",
            "MaxNative",
            "MaxNumber",
            "Min",
            "MinNative",
            "MinNumber",
            "Sign",
            "MaxMagnitude",
            "MaxMagnitudeNumber",
            "MinMagnitude",
            "MinMagnitudeNumber",
            "MultiplyAddEstimate",
            "IsCanonical",
            "IsComplexNumber",
            "IsEvenInteger",
            "IsFinite",
            "IsImaginaryNumber",
            "IsInfinity",
            "IsInteger",
            "IsNaN",
            "IsNegative",
            "IsNegativeInfinity",
            "IsNormal",
            "IsOddInteger",
            "IsPositive",
            "IsPositiveInfinity",
            "IsRealNumber",
            "IsSubnormal",
            "IsZero",
            "TryConvertFromChecked",
            "TryConvertFromSaturating",
            "TryConvertFromTruncating",
            "TryConvertToChecked",
            "TryConvertToSaturating",
            "TryConvertToTruncating",
            "One",
            "Zero",
            "NegativeOne",
            "Radix",
            "AdditiveIdentity",
            "MultiplicativeIdentity",
            "MaxValue",
            "MinValue",
            "Parse",
            "TryParse",
            "ToString",
            "Equals",
            "EqualsObject",
            "GetHashCode",
            "CompareTo",
            "CompareToObject",
            "StructuralEquals",
            "StructuralGetHashCode",
            "StructuralCompareTo",
            "GetFormat",
            "StaticEquals",
            "Compare",
            "CompareOrdinal",
            "Add",
            "Subtract",
            "Multiply",
            "Divide",
            "Remainder",
            "Negate",
            "ToInt32",
            "ToSingle",
            "TrueString",
            "FalseString",
            "HasFlag",
            "op_Addition",
            "op_CheckedAddition",
            "op_Subtraction",
            "op_CheckedSubtraction",
            "op_Multiply",
            "op_CheckedMultiply",
            "op_Division",
            "op_CheckedDivision",
            "op_Modulus",
            "op_UnaryNegation",
            "op_CheckedUnaryNegation",
            "op_UnaryPlus",
            "op_Increment",
            "op_CheckedIncrement",
            "op_Decrement",
            "op_CheckedDecrement",
            "op_Equality",
            "op_Inequality",
            "op_LessThan",
            "op_LessThanOrEqual",
            "op_GreaterThan",
            "op_GreaterThanOrEqual",
            "op_BitwiseAnd",
            "op_BitwiseOr",
            "op_ExclusiveOr",
            "op_OnesComplement",
            "op_LeftShift",
            "op_RightShift",
            "op_UnsignedRightShift",
        };
        internal static bool IsMember(string name) => Members.Contains(name);
        internal static string Resolve(NeoConformingTypeKind kind, string interfaceId, string name)
        {
            if (!InterfaceNames.TryGetValue(interfaceId, out string interfaceName))
                throw new NeoScriptPreExecutionValidationError($"'{interfaceId}' is not a system interface.");
            if (name is "CreateChecked" or "CreateSaturating" or "CreateTruncating")
                return "default";
            if (kind is NeoConformingTypeKind.Int or NeoConformingTypeKind.Decimal
                && name is "ClampNative" or "MaxNative" or "MinNative" or "MultiplyAddEstimate")
                return "default";
            string member = (interfaceName, name) switch
            {
                ("IComparable", "CompareTo") => "CompareToObject",
                ("IStructuralEquatable", "Equals") => "StructuralEquals",
                ("IStructuralEquatable", "GetHashCode") => "StructuralGetHashCode",
                ("IStructuralComparable", "CompareTo") => "StructuralCompareTo",
                _ => name,
            };
            if (!Members.Contains(member))
                throw new NeoScriptPreExecutionValidationError($"{interfaceName}.{name} has no primitive implementation.");
            return member;
        }
    }
}
