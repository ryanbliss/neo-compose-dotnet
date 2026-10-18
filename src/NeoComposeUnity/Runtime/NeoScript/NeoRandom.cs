// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Security.Cryptography;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>A client session's portable Xorshift128 stream. State is never saved.</summary>
    public sealed class NeoRandom
    {
        private uint x, y, z, w;
        private string? identity;
        internal long DrawCount
        {
            get; private set;
        }

        public NeoRandom() : this(EntropySeed()) { }
        public NeoRandom(double seed)
        {
            InitState(seed);
        }

        private static uint EntropySeed()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public void InitState(double seed)
        {
            if (!IsSafeInteger(seed))
                throw ArgumentError("Random.InitState requires a safe integer seed.");
            identity = null;
            unchecked
            {
                x = (uint)(long)seed;
                y = 1812433253U * (x ^ (x >> 30)) + 1;
                z = 1812433253U * (y ^ (y >> 30)) + 2;
                w = 1812433253U * (z ^ (z >> 30)) + 3;
            }
        }

        // A value frame keeps nested replay allocation-free, including an uninitialized caller.
        internal readonly struct IdentityFrame : IDisposable
        {
            private readonly NeoRandom owner;
            private readonly uint x, y, z, w;
            private readonly string? identity;
            internal IdentityFrame(NeoRandom owner, string nextIdentity)
            {
                this.owner = owner;
                x = owner.x;
                y = owner.y;
                z = owner.z;
                w = owner.w;
                identity = owner.identity;
                owner.identity = nextIdentity;
            }
            public void Dispose()
            {
                owner.x = x;
                owner.y = y;
                owner.z = z;
                owner.w = w;
                owner.identity = identity;
            }
        }

        internal IdentityFrame EnterIdentity(string contentIdentity) => new(this, contentIdentity);

        private uint NextWord()
        {
            if (identity is not null)
            {
                uint seed = 2166136261;
                foreach (char codeUnit in identity)
                    seed = unchecked((seed ^ codeUnit) * 16777619);
                InitState(seed);
            }
            uint t = x ^ (x << 11);
            x = y;
            y = z;
            z = w;
            w = w ^ (w >> 19) ^ t ^ (t >> 8);
            DrawCount++;
            return w;
        }

        public float value => (float)((NextWord() >> 8) / 16777215d);

        public int Range(int min, int max) => (int)RangeInt(min, max);

        internal double RangeInt(double min, double max)
        {
            if (!IsInteger(min))
                throw ArgumentError("Random.Range int min must be an integer.");
            if (min < int.MinValue || min > int.MaxValue)
                throw ArgumentError("Random.Range int min must fit int32.");
            if (!IsInteger(max))
                throw ArgumentError("Random.Range int max must be an integer.");
            if (max < int.MinValue || max > int.MaxValue)
                throw ArgumentError("Random.Range int max must fit int32.");
            if (min == max)
                return min;
            long width = Math.Abs((long)max - (long)min);
            long cutoff = (4294967296L / width) * width;
            uint word;
            do
            {
                word = NextWord();
            } while (word >= cutoff);
            long offset = word % width;
            return min < max ? min + offset : min - offset;
        }

        public float Range(float min, float max)
        {
            if (!IsFinite(min))
                throw ArgumentError("Random.Range float min must be finite.");
            if (!IsFinite(max))
                throw ArgumentError("Random.Range float max must be finite.");
            if (min == max)
                return min;
            if (min > max)
            {
                float previous = min;
                min = max;
                max = previous;
            }
            double u = value;
            if (u == 0)
                return min;
            if (u == 1)
                return max;
            float result = (float)((1 - u) * min + u * max);
            if (result < min)
                return min;
            if (result > max)
                return max;
            return result;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool IsSafeInteger(double value) => IsInteger(value) && Math.Abs(value) <= 9007199254740991d;
        private static bool IsInteger(double value) => IsFinite(value) && Math.Truncate(value) == value;
        private static NSGetterRuntimeError ArgumentError(string message) => new(message, NeoErrorContract.NeoArgumentError);
    }
}
