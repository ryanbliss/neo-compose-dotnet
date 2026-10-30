// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private readonly Dictionary<string, List<NeoDelegateValue>> staticChangeListeners = new();
        private readonly Dictionary<string, TypeDefinition?> staticListenerOwners = new();
        private object? staticListenerSchema;

        private TypeDefinition? StaticListenerOwner(string memberId)
        {
            if (!ReferenceEquals(staticListenerSchema, SchemaResolution))
            {
                staticListenerOwners.Clear();
                staticListenerSchema = SchemaResolution;
            }
            if (staticListenerOwners.TryGetValue(memberId, out var found))
                return found;
            if (FindSchemaPlacement(memberId)?.ownerClass is { } owner)
                return staticListenerOwners[memberId] = owner.genericParams is { Count: > 0 }
                    ? new TypeDefinition(MemberKind.Class, classId: owner.id) : null;
            foreach (var contract in data.interfaces.Values)
                if (contract.genericParams is { Count: > 0 }
                    && contract.members.Values.Any(member => member.implementationMemberId == memberId))
                    return staticListenerOwners[memberId] = new TypeDefinition(MemberKind.Interface, interfaceId: contract.id);
            return staticListenerOwners[memberId] = null;
        }

        internal void EditStaticChangeListener(string memberId, Json.TypeInfo observedType, NeoDelegateValue? listener,
            bool add, NSGetterEvaluator.Context ctx)
        {
            if (ctx.constructorBody || constructionListenerCapture is not null || isReplayingVirtualInstance || candidateReplay is not null)
                throw new NSGetterRuntimeError("A static OnChanged subscription lives only for the play session, so a constructor can't change one. Subscribe from a function.");
            if (!TryGetMember(memberId, out Member? member))
                throw new NSGetterRuntimeError($"Observed static member '{memberId}' does not exist.");
            if (member.Modifier != NeoMemberModifierKind.Static)
                throw new NSGetterRuntimeError($"Observed member '{member.name}' is not static.");
            var owner = StaticListenerOwner(memberId)
                ?? throw new NSGetterRuntimeError($"OnChanged requires an instance member. '{member.name}' is static.");
            if (member is NSPropertyMember or NSFunctionMember or FunctionMember or DelegateMember or ActionMember)
                throw new NSGetterRuntimeError($"OnChanged requires a stored field. '{member.name}' is callable or has accessor bodies.");
            if (member.Mutability == NeoMemberMutabilityKind.ReadOnly || ResolveStaticOwnership(member) == NeoValueOwnership.Asset)
                throw new NSGetterRuntimeError($"OnChanged cannot observe readonly static '{member.name}'.");
            if (listener is null)
            {
                staticChangeListeners.Remove(memberId);
                return;
            }
            if (!listener.IsMemberTarget || !TryGetMember(listener.memberId!, out Member? handler))
                throw new NSGetterRuntimeError("A static OnChanged handler must name an existing member target.");
            if (NeoNSFunctionRuntime.ContainsGeneric(observedType))
            {
                if (handler.Modifier != NeoMemberModifierKind.Static)
                    throw new NSGetterRuntimeError($"'{member.name}' has a type per construction, so its OnChanged handler must be static.");
                if (!Equals(owner, StaticListenerOwner(handler.id)))
                    throw new NSGetterRuntimeError($"'{member.name}' has a type per construction, so its OnChanged handler must be declared by its generic owner.");
            }
            ObjectMemberValue? receiver = null;
            if (handler.Modifier != NeoMemberModifierKind.Static
                && (listener.valueId is null || !TryGetValue(listener.valueId, out receiver)))
                throw new NSGetterRuntimeError($"Listener method '{handler.id}' does not belong to a live receiver.");
            var handlerEnvironment = listener.staticConstruction is { } bound
                ? NeoInterfaceDispatch.Of(this).Environment(bound) : null;
            string? error = ChangeListenerSignatureError(handler.id, observedType, handler, receiver,
                typeEnvironment: handlerEnvironment, preserveOpen: Equals(owner, StaticListenerOwner(handler.id)));
            if (error is not null)
                throw new NSGetterRuntimeError(error);
            if (!staticChangeListeners.TryGetValue(memberId, out var handlers))
            {
                if (!add)
                    return;
                staticChangeListeners[memberId] = handlers = new();
            }
            int index = handlers.FindIndex(target => target.memberId == listener.memberId && target.valueId == listener.valueId);
            if (add && index < 0)
                handlers.Add(listener.PersistedCopy());
            else if (!add && index >= 0)
                handlers.RemoveAt(index);
        }

        internal void QueueStaticChange(string memberId, TypeBinding construction)
        {
            if (!staticChangeListeners.TryGetValue(memberId, out var handlers) || handlers.Count == 0)
                return;
            var key = (memberId, NeoInterfaceDispatch.Identity(construction));
            if (pendingListenerChanges.StaticKeys.Add(key))
                pendingListenerChanges.Statics.Add(key);
            pendingListenerChanges.IsEmpty = false;
        }

        private void DispatchStaticChange(string memberId, TypeBinding construction)
        {
            if (!staticChangeListeners.TryGetValue(memberId, out var handlers) || handlers.Count == 0)
                return;
            var snapshot = handlers.ToArray();
            var binding = ConstructedStaticBinding(memberId, construction);
            var context = CreateGetterContext(binding.Ownership);
            context.BindRoot(NeoScriptValueMarshaller.ResolveRoot(this, context));
            object? value = NSGetterEvaluator.EvaluatePointer(new StaticMemberPointer
            {
                memberId = memberId,
                staticConstruction = NeoTypeBindings.ToTypeInfo(construction),
            }, new NeoScriptScope(), context);
            var arguments = new[] { value };
            var owner = StaticListenerOwner(memberId);
            foreach (var handler in snapshot)
            {
                var target = handler.PersistedCopy();
                if (TryGetMember(target.memberId!, out var declaration) && declaration.Modifier == NeoMemberModifierKind.Static
                    && Equals(owner, StaticListenerOwner(declaration.id)))
                    target.staticConstruction = construction;
                NSGetterEvaluator.InvokeDelegate(target, arguments, context);
            }
        }
    }
}
