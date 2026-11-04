// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        private static readonly ConditionalWeakTable<TypeInfo, TypeBinding> generatedConstructions = new();

        internal static TypeBinding ConstructionIdentity(TypeInfo construction) =>
            generatedConstructions.GetValue(construction, type => NeoInterfaceDispatch.Identity(NeoTypeBindings.FromTypeInfo(type)));

        public static NeoStaticBinding StaticBinding(NeoClient client, string memberId, NeoValueOwnership ownership, TypeInfo construction)
        {
            TypeBinding closed = ConstructionIdentity(construction);
            if (closed.Head!.Type == MemberKind.Interface)
            {
                var declaration = NeoInterfaceDispatch.Of(client).Declaration(closed, memberId).Member;
                memberId = declaration.implementationMemberId
                    ?? throw new InvalidOperationException($"Interface member '{memberId}' has no static storage implementation.");
            }
            NeoStaticBinding binding = closed.Bindings is { Count: > 0 }
                ? client.ConstructedStaticBinding(memberId, closed) : client.StaticBinding(memberId);
            binding.RequireOwnership(ownership);
            return binding;
        }
    }
}
