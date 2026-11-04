// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static readonly ConcurrentDictionary<Type, Func<NeoBuiltInComparer, Context, object>> hostComparerFactories = new();

        internal static object ExportBuiltInComparer(NeoBuiltInComparer value, Context context)
        {
            if (value.Kind == NeoBuiltInComparerKind.ReferenceEquality)
                return NeoReferenceEqualityComparer.Instance;
            Type type = NeoGeneratedTypesSupport.ClrTypeOf(context.client, HostComparerArgument(value))
                ?? throw new InvalidOperationException("The comparer element has no generated CLR type.");
            var adapters = ComparerCache(context).HostComparers.GetValue(value, _ => new());
            if (!adapters.TryGetValue(type, out var result))
            {
                var owned = context.client.CreateGetterContext(context.valueOwnership);
                owned.BindRoot(NeoScriptValueMarshaller.ResolveRoot(context.client, owned));
                result = hostComparerFactories.GetOrAdd(type, element =>
                    (Func<NeoBuiltInComparer, Context, object>)typeof(NSGetterEvaluator)
                        .GetMethod(nameof(CreateHostComparer), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(element).CreateDelegate(typeof(Func<NeoBuiltInComparer, Context, object>)))(value, owned);
                adapters.Add(type, result);
            }
            return result;
        }

        private static TypeBinding HostComparerArgument(NeoBuiltInComparer value) => value.Kind == NeoBuiltInComparerKind.SetEquality
            ? TypeBinding.Closed(new TypeHead(MemberKind.Set, true), new Dictionary<string, TypeBinding> { ["0"] = value.Type! })
            : value.Type!;

        // Selected only by reflection; retain the factory in stripped players.
        [UnityEngine.Scripting.Preserve]
        private static object CreateHostComparer<T>(NeoBuiltInComparer value, Context context) =>
            value.Family == NeoComparerFamilyKind.Equality
                ? new HostEqualityComparer<T>(new BoundEquality(HostComparerArgument(value), value, context), context, context.valueOwnership)
                : new HostComparisonComparer<T>(value, context);

        private sealed class HostComparisonComparer<T> : IComparer<T>, IComparer, IHostComparer
        {
            private readonly NeoBuiltInComparer value;
            private readonly Context context;
            private readonly BoundComparison comparison;
            internal HostComparisonComparer(NeoBuiltInComparer value, Context context)
            {
                this.value = value;
                this.context = context;
                comparison = new BoundComparison(value.Type!, value, context);
            }
            NeoClient IHostComparer.Client => context.client;
            object IHostComparer.Value => value;
            private object? Argument(object? item) => item is null ? null : NeoScriptValueMarshaller.Normalize(
                context.client, context.valueOwnership, item, NeoTypeBindings.ToTypeInfo(value.Type!), context, "comparer argument");
            public int Compare(T x, T y) => comparison.Compare(Argument(x), Argument(y));
            int IComparer.Compare(object? x, object? y) => comparison.Compare(Argument(x), Argument(y));
        }
    }
}
