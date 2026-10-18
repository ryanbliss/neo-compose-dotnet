// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private sealed class TupleIdentityRegistry
        {
            private readonly ConditionalWeakTable<object, Identity> objects = new();
            private readonly Dictionary<string, string> rows = new(StringComparer.Ordinal);
            private long nextId;
            private sealed class Identity
            {
                internal readonly string key;
                internal Identity(string key) => this.key = key;
            }
            internal string Key(object? value, string? rowId)
            {
                if (value is not null && objects.TryGetValue(value, out Identity known))
                    return known.key;
                string key = rowId is null ? "object:" + ++nextId
                    : rows.TryGetValue(rowId, out string mapped) ? mapped : "row:" + rowId;
                if (value is not null)
                    objects.Add(value, new Identity(key));
                return key;
            }
            internal void Attach(object value, string rowId)
            {
                if (objects.TryGetValue(value, out Identity known))
                    rows[rowId] = known.key;
            }
        }

        private static readonly ConditionalWeakTable<NeoClient, TupleIdentityRegistry> tupleIdentities = new();

        private static object TupleIdentityObject(object value)
        {
            if (value is NeoGeneratedClassValue { PendingValue: NeoScriptObject pending })
                return pending;
            if (value is RuntimeSet set)
                return set.Source;
            if (value is object?[] array && runtimeLists.TryGetValue(array, out var list))
                return list.origin;
            return value;
        }

        private static string TupleHandleKey(object value, Context context)
        {
            object identity = TupleIdentityObject(value);
            string? rowId = ReferenceKeyOf(value, context) as string;
            return tupleIdentities.GetValue(context.client, _ => new TupleIdentityRegistry()).Key(value is string ? null : identity, rowId);
        }

        internal static void AttachTupleHandle(NeoClient client, object value, string rowId)
        {
            if (tupleIdentities.TryGetValue(client, out TupleIdentityRegistry registry))
                registry.Attach(TupleIdentityObject(value), rowId);
        }
    }
}
