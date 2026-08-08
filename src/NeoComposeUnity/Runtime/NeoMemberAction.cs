// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime wrapper for an NSAction member — a multicast void callable
    /// whose value is an insertion-ordered listener set (P62 §2.1).
    ///
    /// <para>Unlike <see cref="NeoMemberDelegate"/> this node never throws on
    /// an unbound value: the empty listener set, not null, is an action's
    /// rest state, so invoking one with nothing subscribed is a successful
    /// no-op (§3.1).</para>
    /// </summary>
    public class NeoMemberAction : NeoMember<ActionMember, ActionMemberValue>
    {
        /// <summary>
        /// The one <c>NeoAction</c> instance handed out for this node.
        /// Generated properties read the action on every access, and
        /// <c>+=</c> compiles to get → <c>operator+</c> → set, so the getter
        /// must be reference-stable or the setter's identity check (§5.2)
        /// could never pass.
        /// </summary>
        private object? boundAction;

        public NeoMemberAction(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberAction(
            NeoClient client,
            ActionMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        internal object? BoundActionInstance => boundAction;

        /// <summary>
        /// The row that owns this action, as the generated wrapper of that
        /// row spells its own <c>valueId</c>. A C# subscription whose
        /// resolved target row is this one stores <c>valueId: null</c>, which
        /// is byte-identical to what NeoScript's
        /// <c>this.OnX += this.Handler</c> lowers to (P62 §5.2).
        /// </summary>
        internal string? OwningRowValueId
        {
            get
            {
                NeoMember? cursor = parent;
                for (int i = 0; cursor is not null && i < 32; i++)
                {
                    if (cursor.value is ObjectMemberValue row)
                    {
                        return cursor.overrideValueId ?? row.id;
                    }
                    cursor = cursor.parent;
                }
                return null;
            }
        }

        /// <summary>
        /// The stored listener set in invocation order. Never null: an
        /// action with no stored row and no authored default is empty, not
        /// unbound.
        /// </summary>
        public IReadOnlyList<NeoDelegateValue> CurrentListeners() =>
            ResolveActionValue().listeners.ToArray();

        /// <summary>
        /// Fires every listener sequentially in stored order, stopping at the
        /// first one that throws (§3.1 — fail-fast, no log-and-continue).
        /// </summary>
        public void Invoke(params object?[] args)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            // The owning row is threaded in exactly as the evaluator's
            // `callAction` pointer threads it, so a listener stored with a
            // null `valueId` — every declaration default, and every
            // `this.OnX += this.Handler` — binds the row that owns the action
            // whichever language fired it (P62 §3.3).
            object? owner = ResolveLexicalThis(ctx);
            NSGetterEvaluator.InvokeAction(
                ResolveActionValue(),
                args ?? Array.Empty<object?>(),
                ctx.WithThis(owner),
                owner,
                // `{actionMemberName}[{owningRowId ?? "default"}]` — the row
                // the action fanned out from, which is what the evaluator's
                // frame names too. The action's own value-row id would be a
                // different string for the same failure, and an unparented
                // or static action has no owning row at all: "default".
                () => $"{member.name}[{OwningRowValueId ?? "default"}]");
        }

        /// <summary>
        /// The listener set this node reads through: its stored row when one
        /// exists, otherwise the authored declaration default, otherwise the
        /// empty set. The returned value is never handed to a mutator — see
        /// <see cref="NeoMemberActionWritable"/>, which copies before it
        /// writes so a shared declaration default cannot be mutated in place.
        /// </summary>
        internal NeoActionValue ResolveActionValue() =>
            value?.value ?? member.defaultValue?.value ?? new NeoActionValue();

        private protected TAction BindCached<TAction>(Func<TAction> create)
            where TAction : NeoActionBase
        {
            if (boundAction is null)
            {
                TAction created = create();
                boundAction = created;
                return created;
            }
            if (boundAction is TAction bound) return bound;
            throw new InvalidOperationException(
                $"NSAction member '{member.name}' is already bound as {boundAction.GetType().Name} and cannot also bind as {typeof(TAction).Name}; the generated property's arity is the member's declared arity.");
        }

        /// <summary>
        /// Nearest Class-shaped ancestor, resolved the way
        /// <see cref="NeoMemberDelegate"/> resolves it, so a listener reached
        /// through a parented node sees the same lexical <c>this</c> a
        /// delegate call from the same position would.
        /// </summary>
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

        public NeoAction Bind() =>
            BindCached(() => new NeoAction(this));
        public NeoAction<P1> Bind<P1>() =>
            BindCached(() => new NeoAction<P1>(this));
        public NeoAction<P1, P2> Bind<P1, P2>() =>
            BindCached(() => new NeoAction<P1, P2>(this));
        public NeoAction<P1, P2, P3> Bind<P1, P2, P3>() =>
            BindCached(() => new NeoAction<P1, P2, P3>(this));
        public NeoAction<P1, P2, P3, P4> Bind<P1, P2, P3, P4>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4>(this));
        public NeoAction<P1, P2, P3, P4, P5> Bind<P1, P2, P3, P4, P5>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6> Bind<P1, P2, P3, P4, P5, P6>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7> Bind<P1, P2, P3, P4, P5, P6, P7>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> Bind<P1, P2, P3, P4, P5, P6, P7, P8>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15>(this));
        public NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> Bind<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16>() =>
            BindCached(() => new NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16>(this));
    }

    /// <summary>
    /// Writable wrapper for an NSAction member. Every subscription is an
    /// ordinary member-value write of the whole post-mutation listener set
    /// (P62 §3.3) — there is no listener registry anywhere in the SDK, and
    /// the set invariant is enforced here, before the write, because the
    /// strict wire converter rejects a duplicated identity as invalid data.
    /// </summary>
    public sealed class NeoMemberActionWritable : NeoMemberAction
    {
        public NeoMemberActionWritable(
            NeoClient client,
            ActionMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Session)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Appends <paramref name="listener"/> to the stored set. An identity
        /// already present is a no-op — a re-executed subscription path
        /// cannot double-subscribe — and a no-op writes nothing.
        /// </summary>
        public void AddListener(NeoDelegateValue listener)
        {
            string identity = RequireMemberTarget(listener, "add");
            NeoActionValue next = CopyOfStoredValue();
            foreach (NeoDelegateValue existing in next.listeners)
            {
                if (NeoActionValue.ListenerIdentity(existing) == identity) return;
            }
            next.listeners.Add(listener.PersistedCopy());
            Write(next);
        }

        /// <summary>
        /// Drops <paramref name="listener"/>'s identity from the stored set.
        /// An absent identity is a no-op.
        /// </summary>
        public void RemoveListener(NeoDelegateValue listener)
        {
            string identity = RequireMemberTarget(listener, "remove");
            NeoActionValue next = CopyOfStoredValue();
            for (int index = 0; index < next.listeners.Count; index++)
            {
                if (NeoActionValue.ListenerIdentity(next.listeners[index]) != identity)
                {
                    continue;
                }
                next.listeners.RemoveAt(index);
                Write(next);
                return;
            }
        }

        /// <summary>
        /// Replaces the whole listener set — the authoring-shaped write the
        /// value tree performs. Every entry must be a member target and no
        /// two may share an identity; a duplicate is rejected rather than
        /// silently collapsed, because a caller supplying one is working from
        /// a set that was already wrong.
        /// </summary>
        public void SetListeners(IReadOnlyList<NeoDelegateValue> listeners)
        {
            if (listeners is null) throw new ArgumentNullException(nameof(listeners));
            var next = new NeoActionValue();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < listeners.Count; index++)
            {
                string identity = RequireMemberTarget(listeners[index], "store");
                if (!identities.Add(identity))
                {
                    throw new NeoActionListenerException(
                        $"NSAction member '{member.name}' was given listener {index} twice; a listener set holds each (memberId, valueId) once.");
                }
                next.listeners.Add(listeners[index].PersistedCopy());
            }
            Write(next);
        }

        /// <summary>
        /// The stored set, copied. Mutating in place would write through a
        /// shared declaration default — the aliasing bug the copy exists to
        /// prevent — and would also defeat the change notification, which
        /// compares nothing but is raised by the write itself.
        /// </summary>
        private NeoActionValue CopyOfStoredValue() =>
            ResolveActionValue().PersistedCopy();

        private string RequireMemberTarget(NeoDelegateValue listener, string operation)
        {
            if (listener is null)
            {
                throw new NeoActionListenerException(
                    $"Cannot {operation} a null listener on NSAction member '{member.name}'.");
            }
            if (listener.IsClosure)
            {
                throw new NeoActionListenerException(
                    $"Cannot {operation} a closure on NSAction member '{member.name}'; a listener is always a member reference, so extract the body to an NSFunction member and subscribe that.");
            }
            if (!listener.IsMemberTarget)
            {
                throw new NeoActionListenerException(
                    $"Cannot {operation} a listener without a memberId on NSAction member '{member.name}'; only Neo member targets are listeners.");
            }
            return NeoActionValue.ListenerIdentity(listener);
        }

        /// <summary>
        /// The provenance-validated write path <c>NeoMemberDelegateWritable</c>
        /// uses: shadow the row for write, mutate, publish — or mint and bind
        /// a fresh row when nothing is bound yet.
        /// </summary>
        private void Write(NeoActionValue next)
        {
            string nowIso = DateTime.UtcNow.ToString("o");
            ActionMemberValue? writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = next;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                return;
            }
            BindNewValue(new ActionMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = next,
            });
            NotifyChanged();
        }
    }
}
