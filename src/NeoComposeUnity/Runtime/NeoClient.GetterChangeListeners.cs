// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // P106. NeoScript's twin of a generated C# getter watch: a getter
        // subscription hears the memo forget the result its last read
        // armed, then reads the getter again and calls its handlers. It
        // lives in memory for the play session, keyed by the receiver row
        // and the canonical getter, and never reaches a save.
        private sealed class GetterSubscription
        {
            internal readonly string RowId;
            internal readonly string GetterId;
            internal readonly string? ClassId;
            internal readonly object? GenericStamp;
            internal readonly Json.TypeInfo ObservedType;
            internal readonly CallGetterPointer Read;
            internal readonly List<NeoDelegateValue> Targets = new();
            internal readonly Dictionary<(string memberId, string? classId), (object? genericStamp, string? error)> HandlerSignatures = new();
            internal bool Queued;
            // Queued only to arm: its first read calls no handler.
            internal bool Arming;

            internal GetterSubscription(ObjectMemberValue owner, string getterId, Json.TypeInfo observedType)
            {
                RowId = owner.id;
                GetterId = getterId;
                ClassId = owner.classId;
                GenericStamp = owner.genericBindings;
                ObservedType = observedType;
                Read = new CallGetterPointer
                {
                    type = PointerKind.CallGetter,
                    memberId = getterId,
                    receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "listenerOwner" }),
                };
            }
        }

        private readonly Dictionary<string, Dictionary<string, GetterSubscription>> getterSubscriptionsByRow = new(StringComparer.Ordinal);
        private List<GetterSubscription> pendingGetterSubscriptions = new();
        private List<GetterSubscription>? spareGetterSubscriptions;

        // A null listener clears the subscription.
        private void EditGetterChangeListener(ObjectMemberValue owner, Member declaration, Json.TypeInfo observedType,
            NeoDelegateValue? listener, bool add)
        {
            // P106 §2. A function a construction body calls reaches here at runtime.
            string edit = listener is null ? "clear" : "make";
            if (constructionListenerCapture is not null)
                throw new NSGetterRuntimeError($"A getter subscription lives only for the play session, so a constructor can't {edit} one. {(listener is null ? "Clear" : "Subscribe")} from a function.");
            if (isReplayingVirtualInstance)
                throw new NSGetterRuntimeError($"A getter subscription lives only for the play session, so a virtual instance's construction replay can't {edit} one.");
            if (candidateReplay is not null)
                throw new NSGetterRuntimeError($"A getter subscription lives only for the play session, so a candidate construction replay can't {edit} one.");
            string canonical = CanonicalListenerMemberId(declaration);
            if (EffectiveInstanceMember(owner.classId!, canonical) is not NSPropertyMember)
                throw new NSGetterRuntimeError($"Observed getter '{declaration.id}' does not belong to owner '{owner.id}'.");
            if (!TryGetMember(canonical, out Member? root))
                throw new NSGetterRuntimeError($"Observed getter '{canonical}' does not exist.");
            if (root is not NSPropertyMember { returnTypeInfo: { } returnType })
                throw new NSGetterRuntimeError($"Observed getter '{canonical}' declares no return type.");
            Json.TypeInfo actualType = NeoNSFunctionRuntime.ResolveInvocationTypeInfo(this, returnType, ListenerOwnerEnvironment(owner));
            if (!TypeInfoMatches(observedType, actualType))
                throw new NSGetterRuntimeError($"Observed getter '{declaration.id}' no longer matches its compiled type.");
            getterSubscriptionsByRow.TryGetValue(owner.id, out var subscriptions);
            GetterSubscription? subscription = null;
            subscriptions?.TryGetValue(canonical, out subscription);
            if (listener is null)
            {
                if (subscription is null)
                    return;
                // A queued dispatch finds no handlers to call.
                subscription.Targets.Clear();
                DropGetterSubscription(subscription);
                return;
            }
            ValidateChangeListenerHandler(listener, observedType, owner);
            listener = listener.PersistedCopy();
            if (TryGetMember(listener.memberId!, out Member? handler) && handler.Modifier != NeoMemberModifierKind.Static)
                listener.memberId = CanonicalListenerMemberId(handler);
            string identity = NeoActionValue.ListenerIdentity(listener);
            int found = subscription?.Targets.FindIndex(target => NeoActionValue.ListenerIdentity(target) == identity) ?? -1;
            if (!add)
            {
                if (found < 0)
                    return;
                subscription!.Targets.RemoveAt(found);
                if (subscription.Targets.Count == 0)
                    DropGetterSubscription(subscription);
                return;
            }
            if (found >= 0)
                return;
            if (subscription is null)
            {
                subscription = new GetterSubscription(owner, canonical, observedType) { Arming = true };
                if (subscriptions is null)
                    getterSubscriptionsByRow[owner.id] = subscriptions = new(StringComparer.Ordinal);
                subscriptions[canonical] = subscription;
                // The arming read waits for the outermost execution, so it
                // reads committed state and hears every later commit.
                QueueGetterSubscription(subscription);
            }
            subscription.Targets.Add(listener);
            FlushGetterChanges();
        }

        private void QueueGetterSubscription(GetterMemoKey key)
        {
            if (getterSubscriptionsByRow.Count != 0
                && getterSubscriptionsByRow.TryGetValue(key.rowId, out var subscriptions)
                && TryGetMember(key.memberId, out Member? getter)
                && subscriptions.TryGetValue(CanonicalListenerMemberId(getter), out var subscription))
                QueueGetterSubscription(subscription);
        }

        private void QueueGetterSubscription(GetterSubscription subscription)
        {
            if (subscription.Queued)
                return;
            subscription.Queued = true;
            pendingGetterSubscriptions.Add(subscription);
        }

        private void DropGetterSubscription(GetterSubscription subscription)
        {
            if (!getterSubscriptionsByRow.TryGetValue(subscription.RowId, out var subscriptions)
                || !subscriptions.TryGetValue(subscription.GetterId, out var current)
                || !ReferenceEquals(current, subscription))
                return;
            subscriptions.Remove(subscription.GetterId);
            if (subscriptions.Count == 0)
                getterSubscriptionsByRow.Remove(subscription.RowId);
        }

        private void DispatchGetterSubscriptions()
        {
            // A handler's own write queues and dispatches its own changes.
            var draining = pendingGetterSubscriptions;
            pendingGetterSubscriptions = spareGetterSubscriptions ?? new();
            spareGetterSubscriptions = null;
            int next = 0;
            try
            {
                while (next < draining.Count)
                    DispatchGetterSubscription(draining[next++]);
            }
            finally
            {
                // A throw skips the rest of this flush. They stay queued, so
                // the next flush reads them and none goes deaf.
                for (int i = next; i < draining.Count; i++)
                    pendingGetterSubscriptions.Add(draining[i]);
                draining.Clear();
                spareGetterSubscriptions = draining;
            }
        }

        private void DispatchGetterSubscription(GetterSubscription subscription)
        {
            subscription.Queued = false;
            bool notify = !subscription.Arming;
            subscription.Arming = false;
            if (subscription.Targets.Count == 0)
                return;
            // Replacing the row's class or generic bindings makes a new instance.
            if (!TryGetValueOwnership(subscription.RowId, out NeoValueOwnership ownership)
                || !TryGetValue(ownership, subscription.RowId, out MemberValue? row)
                || row is not ObjectMemberValue { classId: not null } owner
                || owner.IsRemoved
                || owner.classId != subscription.ClassId
                || !ReferenceEquals(owner.genericBindings, subscription.GenericStamp))
            {
                DropGetterSubscription(subscription);
                return;
            }
            var context = CreateGetterContext(ownership);
            context.BindRoot(NeoScriptValueMarshaller.ResolveRoot(this, context));
            var scope = new NeoScriptScope();
            object? receiver = NSGetterEvaluator.UnwrapRow(owner, context, ownership);
            scope.SetLocal("listenerOwner", receiver);
            object? value = NSGetterEvaluator.EvalPointer(subscription.Read, scope, context);
            if (!notify)
                return;
            object?[] arguments = { value };
            foreach (NeoDelegateValue target in subscription.Targets.ToArray())
            {
                NeoValueOwnership? receiverScope = null;
                if (target.valueId is string receiverId)
                {
                    if (!TryGetValueOwnership(receiverId, out NeoValueOwnership currentScope))
                    {
                        subscription.Targets.Remove(target);
                        continue;
                    }
                    receiverScope = currentScope;
                }
                Member? handler = ChangeListenerHandler(target, owner, receiverScope, subscription.ObservedType, subscription.HandlerSignatures);
                if (handler is null)
                    continue;
                NeoDelegateValue invocation = target.PersistedCopy();
                invocation.memberId = handler.id;
                NSGetterEvaluator.InvokeDelegate(invocation, arguments, context, receiver, receiverScope);
            }
            // Every handler's receiver is gone, so nothing hears the getter.
            if (subscription.Targets.Count == 0)
                DropGetterSubscription(subscription);
        }
    }
}
