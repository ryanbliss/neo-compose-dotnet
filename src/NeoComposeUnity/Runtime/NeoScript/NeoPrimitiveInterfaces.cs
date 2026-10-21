// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>P127 §4. The primitives that implement a P127 interface.</summary>
    internal enum NeoPrimitive
    {
        Int,
        Float,
        Decimal,
        String,
        Bool,
        Vector2,
        Vector2Int,
        Vector3,
        Vector3Int,
        Color,
    }

    /// <summary>
    /// P127 §4. The canonical primitive interfaces and which primitives
    /// implement them. The C# mirror of neo-compose's
    /// <c>primitive-interfaces.ts</c> and <c>primitive-type-info.ts</c>.
    /// </summary>
    internal static class NeoPrimitiveInterfaces
    {
        // Generated from neo-compose's primitive-interface-conformance.json;
        // PrimitiveInterfaceConformanceTests pins it to the vendored copy.
        internal static readonly IReadOnlyDictionary<string, string> InterfaceIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IEquatable"] = "system_48afe7ca-fe39-41b3-8fa3-cdcb780434ac",
            ["IComparable"] = "system_27194c76-8e29-41c7-b75f-fbe298a69173",
            ["IFormattable"] = "system_1c1e3b22-76cb-4a5b-9c14-70b4504927fc",
            ["IParsable"] = "system_049568c0-7f1d-48b2-afbf-5c266d111693",
            ["IEqualityOperators"] = "system_bb858d3c-2ae7-4eee-8979-2fa2b1ed113b",
            ["IComparisonOperators"] = "system_abcf5861-be29-4daa-941d-50fbd9216d63",
            ["IAdditionOperators"] = "system_ae8b9e70-3066-4b6a-ad3a-4cd220a6dd0b",
            ["ISubtractionOperators"] = "system_862e5098-092f-4173-bf5d-cbc2e771c610",
            ["IMultiplyOperators"] = "system_bbde7744-acac-42dd-9769-7024d773621f",
            ["IDivisionOperators"] = "system_83401caa-b9ab-40b5-9712-ec85ea7f509a",
            ["IModulusOperators"] = "system_5ac03502-80fb-40b8-bf5c-f9a6acf6718b",
            ["IBitwiseOperators"] = "system_74f7e627-8ff3-43b1-ad63-df08d7253fc0",
            ["IShiftOperators"] = "system_9a6fe10c-83b3-4f9d-bc96-2acefc3b7261",
            ["IUnaryNegationOperators"] = "system_e35deff5-6bfc-4af9-a865-d738cbf9a883",
            ["IUnaryPlusOperators"] = "system_26330e0f-435e-42d7-8ee4-ec6a0b8fd6b9",
            ["IIncrementOperators"] = "system_d05342a4-a1b7-4e40-bb34-d691c4087085",
            ["IDecrementOperators"] = "system_57a399dd-b5e5-4aad-abcd-dfab42262139",
            ["IAdditiveIdentity"] = "system_a8af5f41-af52-44ac-8dda-1f2a3dccadf0",
            ["IMultiplicativeIdentity"] = "system_2d3ceba7-2afb-4bf4-8081-255ce98de082",
            ["INumberBase"] = "system_4a1c0fa1-dbfe-4de9-8304-bc54cc55b8de",
            ["INumber"] = "system_1e71630f-2060-451e-9516-8d0ce060666a",
        };

        internal static readonly IReadOnlyDictionary<string, string[]> ParameterIds = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["IEquatable"] = new[] { "system_ce00cf88-c7e0-459d-984d-3f96960ded6a" },
            ["IComparable"] = new[] { "system_a47c203f-b81d-4119-9f97-d49a3b6e2806" },
            ["IFormattable"] = Array.Empty<string>(),
            ["IParsable"] = new[] { "system_3a8e914b-2ffe-46b2-95b9-735407cda26f" },
            ["IEqualityOperators"] = new[] { "system_52294cb0-98d9-4ffe-91bd-7b8cf9f9c536", "system_83877730-6147-4769-bc7d-9cf6a497e8ba", "system_612ae0a1-ad02-48b4-8945-9194a0812788" },
            ["IComparisonOperators"] = new[] { "system_027a8e63-678d-45b9-86b2-1de7d05f3ff8", "system_65bda703-c2cf-45fa-992e-f7a8fdb15da8", "system_35cb62db-b4db-4adb-b7eb-b63f2ae7092c" },
            ["IAdditionOperators"] = new[] { "system_e19a6d2a-de0f-44c8-8b81-1666ea23eb08", "system_9918970e-bc6d-451e-9665-ab0ea74467bd", "system_b090e8bc-9012-4375-a5df-dd3983e42351" },
            ["ISubtractionOperators"] = new[] { "system_6c91ef9b-6dcd-4921-82c0-38bbbb4a2420", "system_4ec388a2-ca50-4646-b662-c72a6898ec0f", "system_d50380df-f818-4a82-b72f-2a52a4c15d62" },
            ["IMultiplyOperators"] = new[] { "system_843b8881-d8ed-4258-b91a-f5951e7e2f83", "system_7510391c-e73b-490c-b03c-e56b6b371b31", "system_c4b9849c-b6b6-414b-ab6f-146af9ddb5b9" },
            ["IDivisionOperators"] = new[] { "system_d9a9f1ad-0def-484d-b968-fb1ffd37089e", "system_ebff1ba7-c2ab-4ef9-9389-7279224805c3", "system_d549c27b-8f02-4806-845b-01349538840f" },
            ["IModulusOperators"] = new[] { "system_a4e24485-c31f-48b6-be82-6b3103d99611", "system_7c018d97-581c-4fc5-8e09-24a108acf49d", "system_e6686429-7cc9-4d7d-a163-58c1fba7db78" },
            ["IBitwiseOperators"] = new[] { "system_79d8354a-9842-49a3-892a-af827c21915c", "system_0295a803-d418-4c6c-be7b-2090e84fbe6e", "system_2b158d53-bcb3-4a72-97ff-db08c495edb7" },
            ["IShiftOperators"] = new[] { "system_870ed5bf-f4e2-48b5-99df-b966de052892", "system_d19fb0a0-f755-461b-9b8f-87e0da60cb07", "system_89a359fa-b9ac-457c-b369-8e9fbd49bd33" },
            ["IUnaryNegationOperators"] = new[] { "system_0fb5cf72-adf9-412e-ab1c-41e91e1f1876", "system_adf25bb9-9356-4e1c-a049-ac73cfea1775" },
            ["IUnaryPlusOperators"] = new[] { "system_77953265-3b92-4d6f-b401-f4ccd8e27c3d", "system_5a6185f3-303c-41e1-9083-25443b5fbaae" },
            ["IIncrementOperators"] = new[] { "system_af2bda94-0113-455b-8d26-4d6247f4f0ed" },
            ["IDecrementOperators"] = new[] { "system_6378c8ba-b650-4599-8679-95539337a61b" },
            ["IAdditiveIdentity"] = new[] { "system_c97428b5-eaeb-421a-8138-6ac4167e5b14", "system_7a71b980-1d86-4a96-a271-33ed1ffd1dca" },
            ["IMultiplicativeIdentity"] = new[] { "system_04836973-4aca-49d6-a31c-d7981d064b2b", "system_41765cd6-1607-467b-8d35-2143948b48e7" },
            ["INumberBase"] = new[] { "system_96a08b47-7d93-4889-87e9-70f4118c45ad" },
            ["INumber"] = new[] { "system_e7cb3c6d-45ac-4e51-8d70-1a55230afc28" },
        };

        internal static readonly IReadOnlyDictionary<string, string> MemberIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Equals"] = "system_bed4a69b-6ff1-4650-8535-7b5a69d3f5e8",
            ["GetHashCode"] = "system_7a8187f4-0cbc-42c0-8de7-3ea1c28b7b17",
            ["CompareTo"] = "system_10af5f24-6df3-4f34-8110-2fa5faa51d4d",
            ["ToString"] = "system_d4f7a3eb-3460-4fc9-86f7-5c75643e019d",
            ["Parse"] = "system_50735a4a-7fb1-4361-9784-912ca186e0ce",
            ["TryParse"] = "system_96cf3c73-adc3-4d9d-9cd9-7727776e6242",
            ["op_Addition"] = "system_b1673883-bcb5-48d1-95af-e6823bc7fae6",
            ["op_Subtraction"] = "system_8c12c309-d5c4-42ca-bb77-86043f925499",
            ["op_Multiply"] = "system_588b00c3-17c1-4799-9f53-717d2df5399c",
            ["op_Division"] = "system_8f7a7bad-9c0e-4d3e-b2a8-bb120111403b",
            ["op_Modulus"] = "system_3a85b696-f27b-4d29-8b42-2e0a850cfb43",
            ["op_BitwiseAnd"] = "system_8f958d92-28aa-4acc-b908-f3b979fbdec0",
            ["op_BitwiseOr"] = "system_fe448d5e-57b3-48f5-9beb-6221329e66aa",
            ["op_ExclusiveOr"] = "system_526526bd-5572-4267-bb14-8f4a6dd09cb9",
            ["op_OnesComplement"] = "system_63eb20a1-3821-4a41-ac74-18acf8e29ab0",
            ["op_LeftShift"] = "system_178b5391-e2e3-4b48-880b-24c7fd9b075f",
            ["op_RightShift"] = "system_de236000-4483-438b-ad2d-bce69e6c12a4",
            ["op_UnsignedRightShift"] = "system_4f9f2cb9-38dc-49c1-81a7-22be364a81b8",
            ["op_UnaryNegation"] = "system_467972b1-cc65-43fa-97fd-6ca9017084cf",
            ["op_UnaryPlus"] = "system_c4c08e90-052a-4d36-886b-56e566bb3475",
            ["op_Increment"] = "system_7e65267d-dbd0-49e5-bf46-7750367068bd",
            ["op_Decrement"] = "system_566981ae-83a9-4f5c-b6fa-33a431d11a7e",
            ["AdditiveIdentity"] = "system_8a6056da-3e56-4bbd-b347-f9f49f7792a7",
            ["MultiplicativeIdentity"] = "system_d6b6ccb1-2ec7-4c1d-a8d5-9efec1a47905",
            ["Zero"] = "system_9876a093-d257-4215-8c5d-af7af24fd527",
            ["One"] = "system_c0082e39-6774-4af8-abb2-c76ace80bad3",
        };

        /// <summary>Each primitive's interface constructions: the interface name, then each argument (<c>S</c> is the primitive itself).</summary>
        internal static readonly IReadOnlyDictionary<string, string[][]> Conformance = new Dictionary<string, string[][]>(StringComparer.Ordinal)
        {
            ["int"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IComparable", "S" },
                new[] { "IEqualityOperators", "S", "S", "bool" },
                new[] { "IComparisonOperators", "S", "S", "bool" },
                new[] { "IFormattable" },
                new[] { "IParsable", "S" },
                new[] { "IAdditionOperators", "S", "S", "S" },
                new[] { "ISubtractionOperators", "S", "S", "S" },
                new[] { "IMultiplyOperators", "S", "S", "S" },
                new[] { "IUnaryNegationOperators", "S", "S" },
                new[] { "IUnaryPlusOperators", "S", "S" },
                new[] { "IIncrementOperators", "S" },
                new[] { "IDecrementOperators", "S" },
                new[] { "IAdditiveIdentity", "S", "S" },
                new[] { "IMultiplicativeIdentity", "S", "S" },
                new[] { "IDivisionOperators", "S", "S", "S" },
                new[] { "IModulusOperators", "S", "S", "S" },
                new[] { "INumberBase", "S" },
                new[] { "INumber", "S" },
                new[] { "IBitwiseOperators", "S", "S", "S" },
                new[] { "IShiftOperators", "S", "int", "S" },
            },
            ["float"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IComparable", "S" },
                new[] { "IEqualityOperators", "S", "S", "bool" },
                new[] { "IComparisonOperators", "S", "S", "bool" },
                new[] { "IFormattable" },
                new[] { "IParsable", "S" },
                new[] { "IAdditionOperators", "S", "S", "S" },
                new[] { "ISubtractionOperators", "S", "S", "S" },
                new[] { "IMultiplyOperators", "S", "S", "S" },
                new[] { "IUnaryNegationOperators", "S", "S" },
                new[] { "IUnaryPlusOperators", "S", "S" },
                new[] { "IIncrementOperators", "S" },
                new[] { "IDecrementOperators", "S" },
                new[] { "IAdditiveIdentity", "S", "S" },
                new[] { "IMultiplicativeIdentity", "S", "S" },
                new[] { "IDivisionOperators", "S", "S", "S" },
                new[] { "IModulusOperators", "S", "S", "S" },
                new[] { "INumberBase", "S" },
                new[] { "INumber", "S" },
            },
            ["decimal"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IComparable", "S" },
                new[] { "IEqualityOperators", "S", "S", "bool" },
                new[] { "IComparisonOperators", "S", "S", "bool" },
                new[] { "IFormattable" },
                new[] { "IParsable", "S" },
                new[] { "IAdditionOperators", "S", "S", "S" },
                new[] { "ISubtractionOperators", "S", "S", "S" },
                new[] { "IMultiplyOperators", "S", "S", "S" },
                new[] { "IUnaryNegationOperators", "S", "S" },
                new[] { "IUnaryPlusOperators", "S", "S" },
                new[] { "IIncrementOperators", "S" },
                new[] { "IDecrementOperators", "S" },
                new[] { "IAdditiveIdentity", "S", "S" },
                new[] { "IMultiplicativeIdentity", "S", "S" },
            },
            ["string"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IComparable", "S" },
                new[] { "IEqualityOperators", "S", "S", "bool" },
                new[] { "IComparisonOperators", "S", "S", "bool" },
                new[] { "IParsable", "S" },
            },
            ["bool"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IComparable", "S" },
                new[] { "IParsable", "S" },
            },
            ["Vector2"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IFormattable" },
            },
            ["Vector2Int"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IFormattable" },
            },
            ["Vector3"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IFormattable" },
            },
            ["Vector3Int"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IFormattable" },
            },
            ["Color"] = new[]
            {
                new[] { "IEquatable", "S" },
                new[] { "IFormattable" },
            },
        };


        /// <summary>P127 §6. Why a primitive can't fill a stored interface member.</summary>
        internal const string StorageMessage =
            "A primitive value cannot be stored in an interface member. Use the primitive type or a generic parameter.";

        internal static readonly string IEquatableId = InterfaceIds["IEquatable"];
        internal static readonly string IEquatableParameterId = ParameterIds["IEquatable"][0];
        internal static readonly string IComparableId = InterfaceIds["IComparable"];

        private static readonly (NeoPrimitive Primitive, string Name, MemberKind Kind)[] Primitives =
        {
            (NeoPrimitive.Int, "int", MemberKind.Int),
            (NeoPrimitive.Float, "float", MemberKind.Float),
            (NeoPrimitive.Decimal, "decimal", MemberKind.Decimal),
            (NeoPrimitive.String, "string", MemberKind.String),
            (NeoPrimitive.Bool, "bool", MemberKind.Bool),
            (NeoPrimitive.Vector2, "Vector2", MemberKind.Vector2),
            (NeoPrimitive.Vector2Int, "Vector2Int", MemberKind.Vector2Int),
            (NeoPrimitive.Vector3, "Vector3", MemberKind.Vector3),
            (NeoPrimitive.Vector3Int, "Vector3Int", MemberKind.Vector3Int),
            (NeoPrimitive.Color, "Color", MemberKind.Color),
        };

        private static readonly Dictionary<string, string> OperatorTokens = new(StringComparer.Ordinal)
        {
            ["op_Addition"] = "+",
            ["op_Subtraction"] = "-",
            ["op_Multiply"] = "*",
            ["op_Division"] = "/",
            ["op_Modulus"] = "%",
            ["op_BitwiseAnd"] = "&",
            ["op_BitwiseOr"] = "|",
            ["op_ExclusiveOr"] = "^",
            ["op_LeftShift"] = "<<",
            ["op_RightShift"] = ">>",
            ["op_UnsignedRightShift"] = ">>>",
            ["op_UnaryPlus"] = "+",
            ["op_UnaryNegation"] = "-",
            ["op_OnesComplement"] = "~",
            ["op_Increment"] = "++",
            ["op_Decrement"] = "--",
        };

        private static readonly Dictionary<string, NeoPrimitive> PrimitiveByName = new(StringComparer.Ordinal);
        private static readonly Dictionary<MemberKind, NeoPrimitive> PrimitiveByKind = new();
        private static readonly Dictionary<string, string> MemberNameById = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> ParameterIdsByInterfaceId = new(StringComparer.Ordinal);

        /// <summary>By interface id, each implementing primitive's constructions' arguments.</summary>
        private static readonly Dictionary<string, Dictionary<NeoPrimitive, List<string[]>>> Implementers = new(StringComparer.Ordinal);

        static NeoPrimitiveInterfaces()
        {
            foreach (var (primitive, name, kind) in Primitives)
            {
                PrimitiveByName.Add(name, primitive);
                PrimitiveByKind.Add(kind, primitive);
            }
            foreach (var member in MemberIds)
                MemberNameById.Add(member.Value, member.Key);
            foreach (var contract in InterfaceIds)
                ParameterIdsByInterfaceId.Add(contract.Value, ParameterIds[contract.Key]);
            foreach (var conformance in Conformance)
            {
                NeoPrimitive primitive = PrimitiveByName[conformance.Key];
                foreach (string[] construction in conformance.Value)
                {
                    string interfaceId = InterfaceIds[construction[0]];
                    if (!Implementers.TryGetValue(interfaceId, out var byPrimitive))
                        Implementers.Add(interfaceId, byPrimitive = new Dictionary<NeoPrimitive, List<string[]>>());
                    if (!byPrimitive.TryGetValue(primitive, out var constructions))
                        byPrimitive.Add(primitive, constructions = new List<string[]>());
                    var arguments = new string[construction.Length - 1];
                    Array.Copy(construction, 1, arguments, 0, arguments.Length);
                    constructions.Add(arguments);
                }
            }
        }

        internal static bool TryParse(string name, out NeoPrimitive primitive) =>
            PrimitiveByName.TryGetValue(name, out primitive);

        internal static bool TryOfKind(MemberKind kind, out NeoPrimitive primitive) =>
            PrimitiveByKind.TryGetValue(kind, out primitive);

        internal static MemberKind KindOf(NeoPrimitive primitive) => Primitives[(int)primitive].Kind;

        internal static string NameOf(NeoPrimitive primitive) => Primitives[(int)primitive].Name;

        /// <summary>The required type of <paramref name="primitive"/>'s values.</summary>
        internal static TypeInfo TypeInfoOf(NeoPrimitive primitive) =>
            new PrimitiveTypeInfo { type = KindOf(primitive), required = true };

        /// <summary>The canonical member name, or null for a member P127 does not declare.</summary>
        internal static string? MemberName(string memberId) =>
            MemberNameById.TryGetValue(memberId, out string? name) ? name : null;

        /// <summary>Source display for a member name: <c>operator +</c> for operators, the name otherwise.</summary>
        internal static string DisplayName(string name) =>
            OperatorTokens.TryGetValue(name, out string? token) ? "operator " + token : name;

        /// <summary>The interface construction an interface or function-argument type names.</summary>
        internal static bool TryConstruction(TypeInfo type, out string interfaceId, out Dictionary<string, TypeInfo>? typeArguments)
        {
            (interfaceId, typeArguments) = type switch
            {
                InterfaceTypeInfo protocol => (protocol.interfaceId, protocol.typeArguments),
                FunctionArgumentTypeInfo { type: MemberKind.Interface, interfaceId: not null } argument => (argument.interfaceId, argument.typeArguments),
                _ => (null!, null),
            };
            return interfaceId is not null;
        }

        /// <summary>P127 §6. An interface type some primitive implements.</summary>
        internal static bool AdmitsPrimitives(TypeInfo type) =>
            TryConstruction(type, out string id, out _) && Implementers.ContainsKey(id);

        /// <summary>P127 §4. Whether <paramref name="primitive"/> implements the closed interface construction.</summary>
        internal static bool Implements(NeoPrimitive primitive, string interfaceId, Dictionary<string, TypeInfo>? typeArguments)
        {
            if (!Implementers.TryGetValue(interfaceId, out var byPrimitive)
                || !byPrimitive.TryGetValue(primitive, out var constructions))
                return false;
            string[] parameterIds = ParameterIdsByInterfaceId[interfaceId];
            foreach (string[] arguments in constructions)
            {
                bool matches = true;
                for (int i = 0; i < arguments.Length && matches; i++)
                {
                    NeoPrimitive expected = arguments[i] == "S" ? primitive : PrimitiveByName[arguments[i]];
                    matches = typeArguments is not null
                        && typeArguments.TryGetValue(parameterIds[i], out TypeInfo? actual)
                        && TryOfKind(actual.type, out NeoPrimitive actualPrimitive)
                        && actualPrimitive == expected;
                }
                if (matches)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// P127 §6. The primitive a closed evidence type names: the primitive
        /// kind itself, or the one primitive implementing an interface
        /// construction. Null when it names a class or more than one primitive.
        /// </summary>
        internal static NeoPrimitive? OfTypeInfo(TypeInfo type)
        {
            if (TryOfKind(type.type, out NeoPrimitive direct))
                return direct;
            if (!TryConstruction(type, out string interfaceId, out var typeArguments)
                || !Implementers.TryGetValue(interfaceId, out var byPrimitive))
                return null;
            NeoPrimitive? match = null;
            foreach (NeoPrimitive primitive in byPrimitive.Keys)
            {
                if (!Implements(primitive, interfaceId, typeArguments))
                    continue;
                if (match is not null)
                    return null;
                match = primitive;
            }
            return match;
        }

        /// <summary>
        /// P127 §12. The type a non-row value of a closed value-usable
        /// interface construction holds: its one primitive, or the tuple an
        /// <c>IEquatable</c> or <c>IComparable</c> is constructed over.
        /// </summary>
        internal static TypeInfo? HeldTypeOf(TypeInfo type)
        {
            if (OfTypeInfo(type) is NeoPrimitive primitive)
                return TypeInfoOf(primitive);
            if (!TryConstruction(type, out string interfaceId, out var typeArguments)
                || (interfaceId != IEquatableId && interfaceId != IComparableId)
                || typeArguments is null)
                return null;
            foreach (TypeInfo argument in typeArguments.Values)
                return argument.type == MemberKind.Tuple ? argument : null;
            return null;
        }

        /// <summary>P127 §6. Whether a <paramref name="primitive"/> value is a <paramref name="type"/>.</summary>
        internal static bool IsType(NeoPrimitive primitive, TypeInfo type)
        {
            if (TryOfKind(type.type, out NeoPrimitive direct))
                return direct == primitive;
            return TryConstruction(type, out string interfaceId, out var typeArguments)
                && Implements(primitive, interfaceId, typeArguments);
        }
    }
}
