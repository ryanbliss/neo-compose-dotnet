// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    internal static class NeoRandomContract
    {
        internal static bool IsSharedNative(string id) => id is Value or RangeInt or RangeFloat or GetSeed or SetSeed;
        internal const string Class = "system_75b4633d-65d0-5da1-ac09-e7c2de0cd102";
        internal const string Value = "system_135e16a9-1996-5e5b-9c2a-730b2896a2cd";
        internal const string RangeInt = "system_60709cd5-8924-5a8c-93e5-482c1e678cdb";
        internal const string RangeFloat = "system_5b15f87c-3ea4-57d8-9156-f12366b73b3b";
        internal const string Seed = "system_e4f25ff7-8b23-5c07-8bcc-6e112fd75575";
        internal const string GetSeed = "system_9d442bde-25e8-51af-baab-08e99947edcd";
        internal const string SetSeed = "system_1eea1de7-5a9b-55e6-8d59-5d27f829d4db";
        internal const string ValidateSeed = "system_a99e8b1e-c2c8-5a49-8497-a4ac9260702c";
    }

    public static partial class NSGetterEvaluator
    {
        /// <summary>Native dispatch used by generated NeoRandom schema methods.</summary>
        public static object? InvokeRandomFunction(NeoClient client, string memberId, object? receiver, object?[] args,
            string receiverKind = CallReceiverKind.Instance)
        {
            Context ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            receiver = UnwrapGeneratedValue(receiver, ctx);
            if (!TryInvokeRandom(memberId, receiverKind, receiver, args, ctx, out object? result))
                throw new NSGetterRuntimeError("Unknown NeoRandom member.");
            return result;
        }

        private static double RandomNumberArg(object? value, string label)
        {
            if (!ArithmeticValue.IsNumeric(value))
                throw new NSGetterRuntimeError($"NeoRandom.{label} must be numeric.", NeoErrorContract.NeoArgumentError);
            return ArithmeticValue.NumberOf(value, 0);
        }

        private static bool TryInvokeRandom(string memberId, string receiverKind, object? receiver, object?[] args,
            Context ctx, out object? result)
        {
            result = null;
            if (memberId is not (NeoRandomContract.Value or NeoRandomContract.RangeInt or NeoRandomContract.RangeFloat
                or NeoRandomContract.GetSeed or NeoRandomContract.SetSeed or NeoRandomContract.ValidateSeed))
                return false;
            if (memberId == NeoRandomContract.ValidateSeed)
            {
                NeoRandom.ValidateSeed(RandomNumberArg(args[0], "seed"));
                return true;
            }
            NeoRandom owner = ctx.Random;
            NeoRandom stream;
            string key = NeoRandomContract.Seed;
            if (receiverKind == CallReceiverKind.Static)
            {
                stream = owner;
            }
            else
            {
                if (receiver is null)
                    throw new NSGetterRuntimeError("NeoRandom instance receiver is null.", NeoErrorContract.NeoNullReferenceError);
                if (FindRowClassIdByReference(receiver, ctx) != NeoRandomContract.Class)
                    throw new NSGetterRuntimeError("NeoRandom requires a NeoRandom instance receiver.");
                RowReference? reference = receiver is NeoScriptObject { attachedId: null } ? null : FindRowReference(receiver, ctx);
                string? id = reference?.valueId;
                NeoValueOwnership ownership = reference?.ownership ?? NeoValueOwnership.Session;
                NeoRandom? existing = owner.FindInstance(receiver, id, ownership);
                if (existing is null)
                {
                    object? seed;
                    if (!(receiver is NeoScriptObject { attachedId: null } detached
                        && TryReadDetachedMember(detached, "_constructorSeed", ctx, out seed)))
                    {
                        seed = DispatchSchemaMember(receiver, "_constructorSeed", ctx);
                        if (!Dispatched(seed))
                            throw new NSGetterRuntimeError("NeoRandom constructor seed is missing.");
                    }
                    existing = owner.AddInstance(receiver, id, RandomNumberArg(seed, "seed"), ownership);
                }
                stream = existing;
                if (memberId is NeoRandomContract.GetSeed or NeoRandomContract.SetSeed)
                {
                    if (id is null && receiver is NeoScriptObject temporary && ctx.client.IsCapturingGetterReads)
                        id = AttachDetached(temporary, ctx);
                    key = id is null ? "" : $"{NeoRandomContract.Seed}:{ownership}:{id}";
                }
            }
            switch (memberId)
            {
                case NeoRandomContract.GetSeed:
                    if (!owner.IsContentScope && key.Length > 0)
                        ctx.client.NoteRowRead(key);
                    result = stream.Seed;
                    break;
                case NeoRandomContract.SetSeed:
                    stream.Seed = RandomNumberArg(args[0], "seed");
                    if (!owner.IsContentScope && key.Length > 0 && !ReferenceEquals(stream, ctx.client.Random))
                        ctx.client.InvalidateGetterMemoForRow(key);
                    break;
                case NeoRandomContract.Value:
                    result = (double)stream.Value();
                    break;
                case NeoRandomContract.RangeInt:
                    result = stream.RangeInt(RandomNumberArg(args[0], "Range min"), RandomNumberArg(args[1], "Range max"));
                    break;
                case NeoRandomContract.RangeFloat:
                    result = (double)stream.Range((float)RandomNumberArg(args[0], "Range min"), (float)RandomNumberArg(args[1], "Range max"));
                    break;
            }
            return true;
        }
    }
}
