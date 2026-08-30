// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>Runtime wrapper for a first-class NeoScript delegate value.</summary>
    public class NeoMemberDelegate
        : NeoMember<DelegateMember, DelegateMemberValue>
    {
        private static readonly ConditionalWeakTable<Delegate, NeoDelegateValue>
            PersistedBindings = new();

        public NeoMemberDelegate(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDelegate(
            NeoClient client,
            DelegateMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public object? Invoke(params object?[] args)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            return InvokeCore(args, ctx.WithThis(ResolveLexicalThis(ctx))).result;
        }

        public object? Invoke(string thisValueId, params object?[] args)
        {
            if (!client.TryGetValue(ownership, thisValueId, out MemberValue? row))
            {
                throw new NSGetterRuntimeError(
                    $"thisValueId '{thisValueId}' was not found in {ownership.ToString().ToLowerInvariant()} values.");
            }
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            object? receiver = NSGetterEvaluator.UnwrapRow(row, ctx, ownership);
            return InvokeCore(args, ctx.WithThis(receiver)).result;
        }

        /// <summary>
        /// Invokes a selector with an explicit lexical owner and resolves its
        /// class-shaped result back to the stored Neo row it came from.
        /// Animation uses this instead of treating a CLR dictionary as a row
        /// id, preserving lexical <c>this</c> for inline selectors.
        /// </summary>
        internal NeoConstructorValueReference? InvokeValueReference(
            string lexicalThisValueId,
            NeoValueOwnership lexicalThisOwnership,
            params object?[] args)
        {
            if (!client.TryGetValue(
                    lexicalThisOwnership,
                    lexicalThisValueId,
                    out MemberValue? row))
            {
                throw new NSGetterRuntimeError(
                    $"Lexical this value '{lexicalThisValueId}' was not found in {lexicalThisOwnership.ToString().ToLowerInvariant()} values.");
            }
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: lexicalThisOwnership);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            object? receiver = NSGetterEvaluator.UnwrapRow(
                row,
                ctx,
                lexicalThisOwnership);
            (object? result, NSGetterEvaluator.Context invocationContext) =
                InvokeCore(args, ctx.WithThis(receiver));
            return NSGetterEvaluator.ConstructorReferenceOf(
                result,
                invocationContext);
        }

        private (object? result, NSGetterEvaluator.Context context) InvokeCore(
            object?[]? args,
            NSGetterEvaluator.Context ctx)
        {
            NeoDelegateValue delegateValue = ResolveDelegateValue();
            return (
                NSGetterEvaluator.InvokeDelegate(
                    delegateValue,
                    args ?? Array.Empty<object?>(),
                    ctx),
                ctx);
        }

        private NeoDelegateValue ResolveDelegateValue() =>
            value?.value
                ?? throw new NSGetterRuntimeError(
                    $"NeoDelegate '{member.name}' has no bound value.");

        private TDelegate TrackBinding<TDelegate>(TDelegate bound)
            where TDelegate : Delegate
        {
            PersistedBindings.Add(bound, ResolveDelegateValue().PersistedCopy());
            return bound;
        }

        internal static NeoDelegateValue? PersistedBindingOf(Delegate? value)
        {
            if (value is null) return null;
            if (!PersistedBindings.TryGetValue(value, out NeoDelegateValue binding))
            {
                throw new ArgumentException(
                    "This delegate was not loaded from a NeoDelegate member and cannot be serialized. Assign a delegate obtained from generated Neo data, or author the binding in Neo Compose.",
                    nameof(value));
            }
            return binding.PersistedCopy();
        }

        private object? ResolveLexicalThis(NSGetterEvaluator.Context ctx)
        {
            NeoMember? cursor = parent;
            for (int i = 0; cursor is not null && i < 32; i++)
            {
                if (cursor.value is ObjectMemberValue row)
                {
                    object? receiver = NSGetterEvaluator.UnwrapRow(
                        row,
                        ctx,
                        cursor.ownership);
                    if (receiver is not null) return receiver;
                }
                cursor = cursor.parent;
            }
            return null;
        }

        public NeoDelegate<TReturn> Bind<TReturn>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn>>(() => convert(Invoke()));
        public NeoDelegate<TReturn, P1> Bind<TReturn, P1>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1>>(p1 => convert(Invoke(p1)));
        public NeoDelegate<TReturn, P1, P2> Bind<TReturn, P1, P2>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2>>((p1, p2) => convert(Invoke(p1, p2)));
        public NeoDelegate<TReturn, P1, P2, P3> Bind<TReturn, P1, P2, P3>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3>>((p1, p2, p3) => convert(Invoke(p1, p2, p3)));
        public NeoDelegate<TReturn, P1, P2, P3, P4> Bind<TReturn, P1, P2, P3, P4>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4>>((p1, p2, p3, p4) => convert(Invoke(p1, p2, p3, p4)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5> Bind<TReturn, P1, P2, P3, P4, P5>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5>>((p1, p2, p3, p4, p5) => convert(Invoke(p1, p2, p3, p4, p5)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6> Bind<TReturn, P1, P2, P3, P4, P5, P6>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6>>((p1, p2, p3, p4, p5, p6) => convert(Invoke(p1, p2, p3, p4, p5, p6)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7>>((p1, p2, p3, p4, p5, p6, p7) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8>>((p1, p2, p3, p4, p5, p6, p7, p8) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9>>((p1, p2, p3, p4, p5, p6, p7, p8, p9) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15)));
        public NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> Bind<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16>(Func<object?, TReturn> convert) =>
            TrackBinding<NeoDelegate<TReturn, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16>>((p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16) => convert(Invoke(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16)));
    }

    /// <summary>Writable wrapper for first-class NeoScript delegate values.</summary>
    public sealed class NeoMemberDelegateWritable : NeoMemberDelegate
    {
        public NeoMemberDelegateWritable(
            NeoClient client,
            DelegateMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Delegate? newValue) =>
            Set(NeoMemberDelegate.PersistedBindingOf(newValue));

        internal void Set(NeoDelegateValue? newValue)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && newValue is null)
            {
                throw new ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)} requirement is Required");
            }
            string nowIso = DateTime.UtcNow.ToString("o");
            DelegateMemberValue? writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                return;
            }
            BindNewValue(new DelegateMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            });
            NotifyChanged();
        }
    }
}
