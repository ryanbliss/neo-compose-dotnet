// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoScript's <c>ReferenceEqualityComparer.Instance</c> in C# (P127 §14).
    /// Unity's .NET Standard 2.1 has no <c>ReferenceEqualityComparer</c>, so
    /// the SDK supplies one with the same interfaces.
    /// </summary>
    public sealed class NeoReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
    {
        private NeoReferenceEqualityComparer()
        {
        }

        public static NeoReferenceEqualityComparer Instance
        {
            get;
        } = new NeoReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object? obj) => obj is null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// NeoScript's <c>Set&lt;T&gt;.CreateSetComparer()</c> in C#, which
    /// transcribes .NET 10's <c>HashSetEqualityComparer&lt;T&gt;</c> (P127
    /// §9.6): two null sets are equal and a null set equals nothing else;
    /// otherwise equal element comparers require matching counts and membership;
    /// different comparers use the default element comparer in a one-way scan. Any two for one <typeparamref name="T"/>
    /// are equal.
    /// </summary>
    public sealed class NeoSetEqualityComparer<T> : IEqualityComparer<INeoReadOnlySet<T>?>
    {
        public bool Equals(INeoReadOnlySet<T>? x, INeoReadOnlySet<T>? y)
        {
            if (x is null)
                return y is null;
            if (y is null)
                return false;
            var first = (x as NeoReadOnlySet<T>)?.Comparer ?? EqualityComparer<T>.Default;
            var second = (y as NeoReadOnlySet<T>)?.Comparer ?? EqualityComparer<T>.Default;
            if (first.Equals(second))
            {
                if (x.Count != y.Count)
                    return false;
                foreach (T item in y)
                    if (!x.Contains(item))
                        return false;
                return true;
            }
            foreach (T item in y)
            {
                bool found = false;
                foreach (T entry in x)
                    if (EqualityComparer<T>.Default.Equals(entry, item))
                    {
                        found = true;
                        break;
                    }
                if (!found)
                    return false;
            }
            return true;
        }

        public int GetHashCode(INeoReadOnlySet<T>? obj)
        {
            int hash = 0;
            if (obj is null)
                return hash;
            foreach (T item in obj)
            {
                if (item is not null)
                    hash ^= item.GetHashCode();
            }
            return hash;
        }

        public override bool Equals(object? obj) => obj is NeoSetEqualityComparer<T>;

        public override int GetHashCode() => typeof(NeoSetEqualityComparer<T>).GetHashCode();
    }

    /// <summary>
    /// A stored <c>EqualityComparer&lt;T&gt;.Create</c> or
    /// <c>Comparer&lt;T&gt;.Create</c> comparer read into C# (P127 §14): it
    /// calls its stored delegates back through the evaluator, and writes back
    /// as the payload it was read from.
    /// </summary>
    internal abstract class NeoScriptComparer
    {
        private readonly NeoClient client;
        private readonly NeoValueOwnership ownership;
        private readonly NeoDelegateValue[] delegates;

        protected NeoScriptComparer(NeoClient client, NeoValueOwnership ownership, JObject payload)
        {
            this.client = client;
            this.ownership = ownership;
            Payload = payload;
            var stored = (JArray)payload["delegates"]!;
            delegates = new NeoDelegateValue[stored.Count];
            for (int index = 0; index < delegates.Length; index++)
                delegates[index] = stored[index].ToObject<NeoDelegateValue>()!;
        }

        /// <summary>The <c>BuiltInComparer</c> payload this comparer stores as.</summary>
        internal JObject Payload
        {
            get;
        }

        protected int DelegateCount => delegates.Length;

        protected object? Invoke(int index, params object?[] args)
        {
            NSGetterEvaluator.Context context = client.CreateGetterContext(ownership);
            context.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, context));
            return NSGetterEvaluator.ResolveHostCollection(NSGetterEvaluator.InvokeDelegate(delegates[index], args, context), context);
        }

        protected bool SameDelegates(NeoScriptComparer other)
        {
            if (delegates.Length != other.delegates.Length)
                return false;
            var context = client.CreateGetterContext(ownership);
            for (int i = 0; i < delegates.Length; i++)
                if (!NSGetterEvaluator.DelegateEquals(delegates[i], other.delegates[i], context))
                    return false;
            return true;
        }

        protected int DelegatesHash()
        {
            var context = client.CreateGetterContext(ownership);
            int first = NSGetterEvaluator.DelegateHash(delegates[0], context);
            int second = delegates.Length > 1 ? NSGetterEvaluator.DelegateHash(delegates[1], context) : 0;
            return unchecked((17 * 31 + first) * 31 + second);
        }

        internal static NeoScriptComparer Create(Type elementType, NeoClient client, NeoValueOwnership ownership, JObject payload, NeoComparerFamilyKind family)
        {
            Type definition = family == NeoComparerFamilyKind.Equality ? typeof(NeoScriptEqualityComparer<>) : typeof(NeoScriptOrderComparer<>);
            return (NeoScriptComparer)Activator.CreateInstance(
                definition.MakeGenericType(elementType),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null,
                new object[] { client, ownership, payload },
                null)!;
        }
    }

    /// <summary>
    /// <c>EqualityComparer&lt;T&gt;.Create(equals, getHashCode?)</c>. Two from
    /// the same delegates are equal, as in .NET 10.
    /// </summary>
    internal sealed class NeoScriptEqualityComparer<T> : NeoScriptComparer, IEqualityComparer<T>, IEqualityComparer
    {
        internal NeoScriptEqualityComparer(NeoClient client, NeoValueOwnership ownership, JObject payload)
            : base(client, ownership, payload)
        {
        }

        public bool Equals(T x, T y) => Convert.ToBoolean(Invoke(0, x, y), System.Globalization.CultureInfo.InvariantCulture);

        public int GetHashCode(T obj)
        {
            if (DelegateCount < 2)
                throw new NotSupportedException("Specified method is not supported.");
            return Convert.ToInt32(Invoke(1, obj), System.Globalization.CultureInfo.InvariantCulture);
        }

        bool IEqualityComparer.Equals(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            if (x is T a && y is T b)
                return Equals(a, b);
            throw new ArgumentException("Type of argument is not compatible with the generic comparer.");
        }

        int IEqualityComparer.GetHashCode(object obj) => obj is null ? 0 : obj is T value ? GetHashCode(value)
            : throw new ArgumentException("Type of argument is not compatible with the generic comparer.");

        public override bool Equals(object? obj) =>
            obj is NeoScriptEqualityComparer<T> other && SameDelegates(other);

        public override int GetHashCode() => DelegatesHash();
    }

    /// <summary><c>Comparer&lt;T&gt;.Create(comparison)</c>, equal only to itself, as in .NET 10.</summary>
    internal sealed class NeoScriptOrderComparer<T> : NeoScriptComparer, IComparer<T>, IComparer
    {
        internal NeoScriptOrderComparer(NeoClient client, NeoValueOwnership ownership, JObject payload)
            : base(client, ownership, payload)
        {
        }

        public int Compare(T x, T y) => Convert.ToInt32(Invoke(0, x, y), System.Globalization.CultureInfo.InvariantCulture);

        int IComparer.Compare(object? x, object? y)
        {
            if (x is null)
                return y is null ? 0 : -1;
            if (y is null)
                return 1;
            if (x is T a && y is T b)
                return Compare(a, b);
            throw new ArgumentException("Type of argument is not compatible with the generic comparer.");
        }
    }
}
