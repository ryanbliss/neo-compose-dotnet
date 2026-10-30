// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>What a table row closes an interface parameter over.</summary>
    internal enum NeoConformanceArgumentKind
    {
        /// <summary><c>S</c>, the conforming type itself.</summary>
        Self,
        Int,
        Bool,
    }

    /// <summary>The types the table lists, each with its .NET 10 in-scope interfaces.</summary>
    internal enum NeoConformingTypeKind
    {
        Int,
        Float,
        Decimal,
        Bool,
        String,
        Vector2,
        Vector2Int,
        Vector3,
        Vector3Int,
        Color,
        /// <summary>Every single-selection enum, <c>NumberStyles</c> included (as <c>System.Enum</c>).</summary>
        Enum,
        /// <summary>Every tuple, whatever its elements (as <c>ValueTuple</c>).</summary>
        Tuple,
        CultureInfo,
        NumberFormatInfo,
    }

    /// <summary>One closed construction a conforming type implements.</summary>
    internal sealed class NeoConformanceConstruction
    {
        internal readonly string InterfaceId;
        internal readonly NeoConformanceArgumentKind[] Arguments;

        internal NeoConformanceConstruction(string interfaceId, NeoConformanceArgumentKind[] arguments)
        {
            InterfaceId = interfaceId;
            Arguments = arguments;
        }
    }

    /// <summary>A closed interface construction a held value implements.</summary>
    internal readonly struct NeoClosedConstruction
    {
        internal readonly string InterfaceId;
        internal readonly NeoRuntimeTypeKey[] Arguments;

        internal NeoClosedConstruction(string interfaceId, NeoRuntimeTypeKey[] arguments)
        {
            InterfaceId = interfaceId;
            Arguments = arguments;
        }
    }

    /// <summary>
    /// P127 §7 and §8.1: the system interface identities and the one
    /// conformance table. Twin of the TypeScript
    /// <c>@neocompose/neoscript-language/primitive-conformance.ts</c>; the
    /// tests hold it equal to <c>primitive-conformance-fixture.json</c>,
    /// copied verbatim from compose. Classes are not in the table (§8.1):
    /// the seeded comparer classes conform through their class records, for
    /// now through <see cref="NeoSystemComparerClasses.InterimConstructions"/>.
    /// </summary>
    internal static class NeoPrimitiveConformance
    {
        internal static NeoConformingTypeKind? Kind(TypeBinding type) => type.Head?.Type switch
        {
            MemberKind.Int => NeoConformingTypeKind.Int,
            MemberKind.Float => NeoConformingTypeKind.Float,
            MemberKind.Decimal => NeoConformingTypeKind.Decimal,
            MemberKind.Bool => NeoConformingTypeKind.Bool,
            MemberKind.String => NeoConformingTypeKind.String,
            MemberKind.Vector2 => NeoConformingTypeKind.Vector2,
            MemberKind.Vector2Int => NeoConformingTypeKind.Vector2Int,
            MemberKind.Vector3 => NeoConformingTypeKind.Vector3,
            MemberKind.Vector3Int => NeoConformingTypeKind.Vector3Int,
            MemberKind.Color => NeoConformingTypeKind.Color,
            MemberKind.Enum => NeoConformingTypeKind.Enum,
            MemberKind.Tuple => NeoConformingTypeKind.Tuple,
            MemberKind.Object when type.Head.ObjectType == NeoObjectTypeKind.Enum => NeoConformingTypeKind.Enum,
            MemberKind.Object when type.Head.ObjectType == NeoObjectTypeKind.CultureInfo => NeoConformingTypeKind.CultureInfo,
            MemberKind.Object when type.Head.ObjectType == NeoObjectTypeKind.NumberFormatInfo => NeoConformingTypeKind.NumberFormatInfo,
            _ => null,
        };
        /// <summary>The 31 system interfaces (§7.1–§7.3), keyed by .NET metadata name, in table order.</summary>
        internal static readonly IReadOnlyList<KeyValuePair<string, string>> InterfaceIds = new[]
        {
            Interface("IEquatable`1", "system_48afe7ca-fe39-41b3-8fa3-cdcb780434ac"),
            Interface("IComparable", "system_cf3f1f8f-5348-4c26-ac16-6bddcab114d0"),
            Interface("IComparable`1", "system_27194c76-8e29-41c7-b75f-fbe298a69173"),
            Interface("IFormatProvider", "system_41a2600d-6f0c-49c4-aa0e-c3cddb91543d"),
            Interface("IFormattable", "system_1c1e3b22-76cb-4a5b-9c14-70b4504927fc"),
            Interface("IParsable`1", "system_049568c0-7f1d-48b2-afbf-5c266d111693"),
            Interface("IEqualityComparer`1", "system_7b45289d-274c-47a7-a924-37560a6b41ef"),
            Interface("IComparer`1", "system_1f354afd-4ed0-419c-8391-d64b935bdd2f"),
            Interface("IEqualityComparer", "system_ea60b68e-c1f3-4d05-b801-aa93b949a6ae"),
            Interface("IComparer", "system_fd3b283d-2783-483a-b8ab-5d26b5235fe1"),
            Interface("IStructuralEquatable", "system_2c8cda3a-6633-4957-8fa4-0304b72a8269"),
            Interface("IStructuralComparable", "system_bd5e50c5-6ffc-44fe-b972-d4f942635c73"),
            Interface("IEqualityOperators`3", "system_bb858d3c-2ae7-4eee-8979-2fa2b1ed113b"),
            Interface("IComparisonOperators`3", "system_abcf5861-be29-4daa-941d-50fbd9216d63"),
            Interface("IAdditionOperators`3", "system_ae8b9e70-3066-4b6a-ad3a-4cd220a6dd0b"),
            Interface("ISubtractionOperators`3", "system_862e5098-092f-4173-bf5d-cbc2e771c610"),
            Interface("IMultiplyOperators`3", "system_bbde7744-acac-42dd-9769-7024d773621f"),
            Interface("IDivisionOperators`3", "system_83401caa-b9ab-40b5-9712-ec85ea7f509a"),
            Interface("IModulusOperators`3", "system_5ac03502-80fb-40b8-bf5c-f9a6acf6718b"),
            Interface("IBitwiseOperators`3", "system_74f7e627-8ff3-43b1-ad63-df08d7253fc0"),
            Interface("IShiftOperators`3", "system_9a6fe10c-83b3-4f9d-bc96-2acefc3b7261"),
            Interface("IUnaryNegationOperators`2", "system_e35deff5-6bfc-4af9-a865-d738cbf9a883"),
            Interface("IUnaryPlusOperators`2", "system_26330e0f-435e-42d7-8ee4-ec6a0b8fd6b9"),
            Interface("IIncrementOperators`1", "system_d05342a4-a1b7-4e40-bb34-d691c4087085"),
            Interface("IDecrementOperators`1", "system_57a399dd-b5e5-4aad-abcd-dfab42262139"),
            Interface("IAdditiveIdentity`2", "system_a8af5f41-af52-44ac-8dda-1f2a3dccadf0"),
            Interface("IMultiplicativeIdentity`2", "system_2d3ceba7-2afb-4bf4-8081-255ce98de082"),
            Interface("IMinMaxValue`1", "system_c4a32e7a-41a2-4d14-a6a0-b881b97b262e"),
            Interface("INumberBase`1", "system_4a1c0fa1-dbfe-4de9-8304-bc54cc55b8de"),
            Interface("INumber`1", "system_1e71630f-2060-451e-9516-8d0ce060666a"),
            Interface("ISignedNumber`1", "system_48851042-2703-4c9c-8f95-362c80f29660"),
        };

        /// <summary>§7.4's <c>System.Globalization.NumberStyles</c> system enum.</summary>
        internal const string NumberStylesEnumId = "system_727c12c2-6d52-4b54-866e-f16b1fef95f1";

        private static readonly Dictionary<string, string> IdsByName = InterfaceIds.ToDictionary(entry => entry.Key, entry => entry.Value);

        /// <summary>
        /// The interfaces whose one type parameter is contravariant
        /// (<c>in T</c>), so a construction over a reference type <c>A</c>
        /// also serves any reference type that converts to <c>A</c> (§4.8).
        /// </summary>
        internal static readonly HashSet<string> ContravariantInterfaceIds = new HashSet<string>
        {
            IdsByName["IComparable`1"],
            IdsByName["IEqualityComparer`1"],
            IdsByName["IComparer`1"],
        };

        private const NeoConformanceArgumentKind Self = NeoConformanceArgumentKind.Self;
        private const NeoConformanceArgumentKind Int = NeoConformanceArgumentKind.Int;
        private const NeoConformanceArgumentKind Bool = NeoConformanceArgumentKind.Bool;

        /// <summary><c>INumber&lt;S&gt;</c> and the closure §7.3's bases give it: 20 constructions.</summary>
        private static readonly NeoConformanceConstruction[] NumberClosure =
        {
            Construction("INumber`1", Self),
            Construction("IComparable"),
            Construction("IComparable`1", Self),
            Construction("IComparisonOperators`3", Self, Self, Bool),
            Construction("IEqualityOperators`3", Self, Self, Bool),
            Construction("IModulusOperators`3", Self, Self, Self),
            Construction("INumberBase`1", Self),
            Construction("IAdditionOperators`3", Self, Self, Self),
            Construction("IAdditiveIdentity`2", Self, Self),
            Construction("IDecrementOperators`1", Self),
            Construction("IDivisionOperators`3", Self, Self, Self),
            Construction("IEquatable`1", Self),
            Construction("IIncrementOperators`1", Self),
            Construction("IMultiplicativeIdentity`2", Self, Self),
            Construction("IMultiplyOperators`3", Self, Self, Self),
            Construction("ISubtractionOperators`3", Self, Self, Self),
            Construction("IUnaryPlusOperators`2", Self, Self),
            Construction("IUnaryNegationOperators`2", Self, Self),
            Construction("IFormattable"),
            Construction("IParsable`1", Self),
        };

        private static readonly NeoConformanceConstruction[] SignedNumber = NumberClosure
            .Concat(new[] { Construction("IMinMaxValue`1", Self), Construction("ISignedNumber`1", Self) })
            .ToArray();

        private static readonly NeoConformanceConstruction[] ComparableParsable =
        {
            Construction("IComparable"),
            Construction("IComparable`1", Self),
            Construction("IEquatable`1", Self),
            Construction("IParsable`1", Self),
        };

        private static readonly NeoConformanceConstruction[] UnityValue =
        {
            Construction("IEquatable`1", Self),
            Construction("IFormattable"),
        };

        private static readonly NeoConformanceConstruction[] FormatProvider =
        {
            Construction("IFormatProvider"),
        };

        /// <summary>
        /// §8.1's table. A value conforms only through its own row: no
        /// numeric conversion comes first (§4.5).
        /// </summary>
        internal static readonly IReadOnlyDictionary<NeoConformingTypeKind, NeoConformanceConstruction[]> Table =
            new Dictionary<NeoConformingTypeKind, NeoConformanceConstruction[]>
            {
                [NeoConformingTypeKind.Int] = SignedNumber
                    .Concat(new[] { Construction("IBitwiseOperators`3", Self, Self, Self), Construction("IShiftOperators`3", Self, Int, Self) })
                    .ToArray(),
                [NeoConformingTypeKind.Float] = SignedNumber
                    .Concat(new[] { Construction("IBitwiseOperators`3", Self, Self, Self) })
                    .ToArray(),
                [NeoConformingTypeKind.Decimal] = SignedNumber,
                [NeoConformingTypeKind.Bool] = ComparableParsable,
                [NeoConformingTypeKind.String] = ComparableParsable,
                [NeoConformingTypeKind.Vector2] = UnityValue,
                [NeoConformingTypeKind.Vector2Int] = UnityValue,
                [NeoConformingTypeKind.Vector3] = UnityValue,
                [NeoConformingTypeKind.Vector3Int] = UnityValue,
                [NeoConformingTypeKind.Color] = UnityValue,
                [NeoConformingTypeKind.Enum] = new[]
                {
                    Construction("IComparable"),
                    Construction("IFormattable"),
                },
                [NeoConformingTypeKind.Tuple] = new[]
                {
                    Construction("IEquatable`1", Self),
                    Construction("IComparable`1", Self),
                    Construction("IComparable"),
                    Construction("IStructuralEquatable"),
                    Construction("IStructuralComparable"),
                },
                [NeoConformingTypeKind.CultureInfo] = FormatProvider,
                [NeoConformingTypeKind.NumberFormatInfo] = FormatProvider,
            };

        /// <summary>The id of system interface <paramref name="name"/>, by .NET metadata name.</summary>
        internal static string InterfaceId(string name) => IdsByName[name];

        /// <summary>
        /// The table's constructions of <paramref name="interfaceId"/> for
        /// <paramref name="kind"/>, closed over <paramref name="self"/> as
        /// <c>S</c>: the TypeScript <c>tableConstructions</c>.
        /// </summary>
        internal static List<NeoClosedConstruction> TableConstructions(NeoConformingTypeKind kind, NeoRuntimeTypeKey self, string interfaceId)
        {
            var constructions = new List<NeoClosedConstruction>();
            foreach (NeoConformanceConstruction row in Table[kind])
            {
                if (row.InterfaceId != interfaceId)
                    continue;
                var arguments = new NeoRuntimeTypeKey[row.Arguments.Length];
                for (int index = 0; index < arguments.Length; index++)
                {
                    arguments[index] = row.Arguments[index] switch
                    {
                        NeoConformanceArgumentKind.Self => self,
                        NeoConformanceArgumentKind.Int => Builtin("int"),
                        _ => Builtin("bool"),
                    };
                }
                constructions.Add(new NeoClosedConstruction(row.InterfaceId, arguments));
            }
            return constructions;
        }

        internal static NeoRuntimeTypeKey Builtin(string name) => new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Builtin, Name = name };

        private static KeyValuePair<string, string> Interface(string name, string id) => new KeyValuePair<string, string>(name, id);

        private static NeoConformanceConstruction Construction(string name, params NeoConformanceArgumentKind[] arguments) =>
            new NeoConformanceConstruction(IdsByName[name], arguments);
    }
}
