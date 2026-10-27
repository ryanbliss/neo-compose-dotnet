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

        public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
    }

    /// <summary>
    /// NeoScript's <c>Set&lt;T&gt;.CreateSetComparer()</c> in C#, which
    /// transcribes .NET 10's <c>HashSetEqualityComparer&lt;T&gt;</c> (P127
    /// §9.6): two null sets are equal and a null set equals nothing else;
    /// otherwise the sets must have the same count and every element of the
    /// second must be in the first. Any two for one <typeparamref name="T"/>
    /// are equal.
    /// </summary>
    public sealed class NeoSetEqualityComparer<T> : IEqualityComparer<INeoReadOnlySet<T>?>
    {
        public bool Equals(INeoReadOnlySet<T>? x, INeoReadOnlySet<T>? y)
        {
            if (x is null)
                return y is null;
            if (y is null || x.Count != y.Count)
                return false;
            foreach (T item in y)
            {
                if (!x.Contains(item))
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
                throw new NotSupportedException("This EqualityComparer<T>.Create comparer has no getHashCode delegate.");
            return Convert.ToInt32(Invoke(1, obj), System.Globalization.CultureInfo.InvariantCulture);
        }

        bool IEqualityComparer.Equals(object? x, object? y) => Equals((T)x!, (T)y!);

        int IEqualityComparer.GetHashCode(object obj) => GetHashCode((T)obj);

        public override bool Equals(object? obj) =>
            obj is NeoScriptEqualityComparer<T> other && JToken.DeepEquals(Payload["delegates"], other.Payload["delegates"]);

        public override int GetHashCode() => Payload["delegates"]!.ToString(Newtonsoft.Json.Formatting.None).GetHashCode();
    }

    /// <summary><c>Comparer&lt;T&gt;.Create(comparison)</c>, equal only to itself, as in .NET 10.</summary>
    internal sealed class NeoScriptOrderComparer<T> : NeoScriptComparer, IComparer<T>, IComparer
    {
        internal NeoScriptOrderComparer(NeoClient client, NeoValueOwnership ownership, JObject payload)
            : base(client, ownership, payload)
        {
        }

        public int Compare(T x, T y) => Convert.ToInt32(Invoke(0, x, y), System.Globalization.CultureInfo.InvariantCulture);

        int IComparer.Compare(object? x, object? y) => Compare((T)x!, (T)y!);
    }
}
