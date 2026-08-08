// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Shared plumbing for the <c>NeoAction</c> registry-class family
    /// (P62 §5.1). An action member is a listener <i>set</i>, not a callable
    /// value, so the generated surface is a class rather than a delegate:
    /// a CLR multicast delegate has no removable, deduplicable identity and
    /// cannot round-trip through Neo data.
    ///
    /// <para>Every mutation here is an ordinary durable member-value write
    /// through <see cref="NeoMemberActionWritable"/> (§3.3) — the SDK holds
    /// no listener state of its own, so a save round trip changes
    /// nothing.</para>
    /// </summary>
    public abstract class NeoActionBase
    {
        private readonly NeoMemberAction node;

        private protected NeoActionBase(NeoMemberAction node)
        {
            this.node = node ?? throw new ArgumentNullException(nameof(node));
        }

        internal NeoMemberAction Node => node;

        /// <summary>
        /// The stored listener set in invocation order — authored entries
        /// first, then runtime subscriptions in the order they were written.
        /// </summary>
        public IReadOnlyList<NeoDelegateValue> Listeners => node.CurrentListeners();

        private protected void AddListenerCore(Delegate listener) =>
            Writable("add a listener to").AddListener(
                NeoGeneratedTypesSupport.ListenerTargetOf(
                    listener,
                    node.OwningRowValueId));

        private protected void AddListenerCore(NeoDelegateValue listener) =>
            Writable("add a listener to").AddListener(listener);

        private protected void RemoveListenerCore(Delegate listener) =>
            Writable("remove a listener from").RemoveListener(
                NeoGeneratedTypesSupport.ListenerTargetOf(
                    listener,
                    node.OwningRowValueId));

        private protected void RemoveListenerCore(NeoDelegateValue listener) =>
            Writable("remove a listener from").RemoveListener(listener);

        private protected void InvokeCore(object?[] args) => node.Invoke(args);

        private protected static void RequireOperand(NeoActionBase? action)
        {
            if (action is not null) return;
            throw new ArgumentNullException(
                nameof(action),
                "Cannot subscribe to a null NeoAction. Read the action from its generated property, which never returns null.");
        }

        /// <summary>
        /// A subscription is a member-value write, so it needs the writable
        /// node — the same rule the generated wrapper enforces with
        /// <c>ThrowIfReadOnly</c> before it ever reaches here.
        /// </summary>
        private NeoMemberActionWritable Writable(string operation)
        {
            if (node is NeoMemberActionWritable writable) return writable;
            throw new InvalidOperationException(
                $"Cannot {operation} NSAction member '{node.member.name}' because its node is read-only; subscriptions are durable member-value writes and require a Save or Session view.");
        }
    }

    /// <summary>Multicast void action with no parameters.</summary>
    public sealed class NeoAction : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke() => InvokeCore(Array.Empty<object?>());

        public static NeoAction operator +(NeoAction action, Action listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction operator +(NeoAction action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction operator -(NeoAction action, Action listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction operator -(NeoAction action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 1 parameter.</summary>
    public sealed class NeoAction<P1> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1) => InvokeCore(new object?[] { p1 });

        public static NeoAction<P1> operator +(NeoAction<P1> action, Action<P1> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1> operator +(NeoAction<P1> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1> operator -(NeoAction<P1> action, Action<P1> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1> operator -(NeoAction<P1> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 2 parameters.</summary>
    public sealed class NeoAction<P1, P2> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2) => InvokeCore(new object?[] { p1, p2 });

        public static NeoAction<P1, P2> operator +(NeoAction<P1, P2> action, Action<P1, P2> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2> operator +(NeoAction<P1, P2> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2> operator -(NeoAction<P1, P2> action, Action<P1, P2> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2> operator -(NeoAction<P1, P2> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 3 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3) => InvokeCore(new object?[] { p1, p2, p3 });

        public static NeoAction<P1, P2, P3> operator +(NeoAction<P1, P2, P3> action, Action<P1, P2, P3> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3> operator +(NeoAction<P1, P2, P3> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3> operator -(NeoAction<P1, P2, P3> action, Action<P1, P2, P3> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3> operator -(NeoAction<P1, P2, P3> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 4 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4) => InvokeCore(new object?[] { p1, p2, p3, p4 });

        public static NeoAction<P1, P2, P3, P4> operator +(NeoAction<P1, P2, P3, P4> action, Action<P1, P2, P3, P4> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4> operator +(NeoAction<P1, P2, P3, P4> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4> operator -(NeoAction<P1, P2, P3, P4> action, Action<P1, P2, P3, P4> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4> operator -(NeoAction<P1, P2, P3, P4> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 5 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5) => InvokeCore(new object?[] { p1, p2, p3, p4, p5 });

        public static NeoAction<P1, P2, P3, P4, P5> operator +(NeoAction<P1, P2, P3, P4, P5> action, Action<P1, P2, P3, P4, P5> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5> operator +(NeoAction<P1, P2, P3, P4, P5> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5> operator -(NeoAction<P1, P2, P3, P4, P5> action, Action<P1, P2, P3, P4, P5> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5> operator -(NeoAction<P1, P2, P3, P4, P5> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 6 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6 });

        public static NeoAction<P1, P2, P3, P4, P5, P6> operator +(NeoAction<P1, P2, P3, P4, P5, P6> action, Action<P1, P2, P3, P4, P5, P6> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6> operator +(NeoAction<P1, P2, P3, P4, P5, P6> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6> operator -(NeoAction<P1, P2, P3, P4, P5, P6> action, Action<P1, P2, P3, P4, P5, P6> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6> operator -(NeoAction<P1, P2, P3, P4, P5, P6> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 7 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7> action, Action<P1, P2, P3, P4, P5, P6, P7> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7> action, Action<P1, P2, P3, P4, P5, P6, P7> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 8 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> action, Action<P1, P2, P3, P4, P5, P6, P7, P8> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> action, Action<P1, P2, P3, P4, P5, P6, P7, P8> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 9 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 10 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 11 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 12 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 13 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 14 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 15 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14, P15 p15) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }

    /// <summary>Multicast void action with 16 parameters.</summary>
    public sealed class NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> : NeoActionBase
    {
        internal NeoAction(NeoMemberAction node) : base(node) { }

        public void AddListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> listener) => AddListenerCore(listener);
        public void AddListener(NeoDelegateValue listener) => AddListenerCore(listener);
        public void RemoveListener(Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> listener) => RemoveListenerCore(listener);
        public void RemoveListener(NeoDelegateValue listener) => RemoveListenerCore(listener);
        public void Invoke(P1 p1, P2 p2, P3 p3, P4 p4, P5 p5, P6 p6, P7 p7, P8 p8, P9 p9, P10 p10, P11 p11, P12 p12, P13 p13, P14 p14, P15 p15, P16 p16) => InvokeCore(new object?[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16 });

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> operator +(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.AddListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> action, Action<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }

        public static NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> operator -(NeoAction<P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16> action, NeoDelegateValue listener)
        {
            RequireOperand(action);
            action.RemoveListener(listener);
            return action;
        }
    }
}
