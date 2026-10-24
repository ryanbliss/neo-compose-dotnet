// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>A fixed tuple value. Generated accessors project immutable snapshots.</summary>
    public class NeoMemberTuple : NeoMember<TupleMember, ArrayMemberValue>
    {
        public NeoMemberTuple(NeoClient client, TupleMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        internal RuntimeTuple? ReadTuple()
        {
            ObserveScriptWrites();
            if (value is not { } row)
                return null;
            NSGetterEvaluator.Context context = client.RentDirectFunctionContext(ownership);
            object? result = null;
            try
            {
                result = NSGetterEvaluator.ResolveHostCollection(
                    NSGetterEvaluator.UnwrapRow(row, context, ownership, member), context);
                return (RuntimeTuple?)result;
            }
            finally
            {
                client.ReturnDirectFunctionContext(context, result);
            }
        }
    }

    public sealed class NeoMemberTupleWritable : NeoMemberTuple
    {
        public NeoMemberTupleWritable(NeoClient client, TupleMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Session)
            : base(client, member, overrideValueId, ownership) { }
    }
}
