// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>
    /// P127 §8.4's <c>GetHashCode</c> for the value kinds. <c>float</c> and
    /// <c>decimal</c> transcribe .NET 10's <c>Single.GetHashCode</c> and
    /// <c>DecCalc.GetHashCode</c> (dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74); the vectors and
    /// <c>Color</c> transcribe the IL of Unity 6000.6.4f1's
    /// UnityEngine.CoreModule.dll. The compose twin is <c>hashing/hashes.ts</c>.
    /// </summary>
    internal static class NeoHashes
    {
        /// <summary>The deterministic combine's seed (§8.4).</summary>
        internal const int Seed = 17;

        /// <summary>One step of the deterministic combine: <c>ToInt32(h * 31 + x)</c>.</summary>
        internal static int Combine(int h, int x) => unchecked((h * 31) + x);

        internal static int Bool(bool value) => value ? 1 : 0;

        /// <summary><c>float.GetHashCode()</c>: the bit pattern, with every NaN and both zeros normalized.</summary>
        internal static int Float(float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            return ((bits - 1) & 0x7FFFFFFF) >= 0x7F800000 ? bits & 0x7F800000 : bits;
        }

        /// <summary>
        /// <c>decimal.GetHashCode()</c> of canonical decimal text: zero
        /// (either sign) is 0; otherwise the trailing fractional zeros are
        /// stripped (<c>Unscale</c>) and the flags and three coefficient words
        /// are XORed.
        /// </summary>
        internal static int Decimal(string value)
        {
            bool negative = value[0] == '-';
            int point = value.IndexOf('.');
            int end = value.Length;
            int scale = point < 0 ? 0 : end - point - 1;
            while (scale > 0 && value[end - 1] == '0')
            {
                end--;
                scale--;
            }
            uint lo = 0;
            uint mid = 0;
            uint hi = 0;
            for (int i = negative ? 1 : 0; i < end; i++)
            {
                if (i == point)
                    continue;
                ulong t = ((ulong)lo * 10) + (uint)(value[i] - '0');
                lo = (uint)t;
                t = ((ulong)mid * 10) + (t >> 32);
                mid = (uint)t;
                hi = unchecked((hi * 10) + (uint)(t >> 32));
            }
            if ((lo | mid | hi) == 0)
                return 0;
            uint flags = (negative ? 0x80000000u : 0) | ((uint)scale << 16);
            return unchecked((int)(flags ^ hi ^ mid ^ lo));
        }

        /// <summary><c>string.GetHashCode()</c>: <c>h = ToInt32(h * 31 + c)</c> over UTF-16 code units, from 0.</summary>
        internal static int String(string value)
        {
            int h = 0;
            foreach (char c in value)
                h = unchecked((h * 31) + c);
            return h;
        }

        /// <summary>Unity's <c>Vector2.GetHashCode()</c>.</summary>
        internal static int Vector2(float x, float y) => Float(x) ^ (Float(y) << 2);

        /// <summary>Unity's <c>Vector3.GetHashCode()</c>.</summary>
        internal static int Vector3(float x, float y, float z) => Float(x) ^ (Float(y) << 2) ^ (Float(z) >> 2);

        /// <summary>Unity's <c>Vector2Int.GetHashCode()</c>.</summary>
        internal static int Vector2Int(int x, int y) => unchecked((x * 73856093) ^ (y * 83492791));

        /// <summary>Unity's <c>Vector3Int.GetHashCode()</c>.</summary>
        internal static int Vector3Int(int x, int y, int z) => x ^ (y << 4) ^ (y >> 28) ^ (z >> 4) ^ (z << 28);

        /// <summary>Unity's <c>Color.GetHashCode()</c>.</summary>
        internal static int Color(float r, float g, float b, float a) =>
            Float(r) ^ (Float(g) << 2) ^ (Float(b) >> 2) ^ (Float(a) >> 1);
    }
}
