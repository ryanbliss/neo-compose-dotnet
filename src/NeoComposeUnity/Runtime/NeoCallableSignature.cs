// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>Callable identity for stored overload validation and closed interface mapping.</summary>
    internal static class NeoCallableSignature
    {
        internal static string Name(string key)
        {
            int separator = key.IndexOf('#');
            return separator < 0 ? key : key.Substring(0, separator);
        }

        // Open T and T? share their parameter identity, as C# requires for an unconstrained T.
        internal static TypeBinding TypeIdentity(TypeInfo type) => type is VoidTypeInfo
            ? TypeBinding.Closed(new TypeHead(MemberKind.Void, true))
            : Erase(NeoTypeBindings.SignatureOf(type));

        private static TypeBinding Erase(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return binding;
            var head = binding.Head!.WithoutNames();
            if (!TypeBindings.IsValueType(head))
                head = head.WithRequired(true);
            return TypeBinding.Closed(head, binding.Bindings?.ToDictionary(pair => pair.Key, pair => Erase(pair.Value)));
        }

        internal static string ParametersKey(int arity, IEnumerable<FunctionArgumentTypeInfo> parameters) =>
            arity + ":" + string.Join(";", parameters.Select(parameter =>
                (parameter.mode?.ToString() ?? "value") + ":" + NeoTypeSources.Key(TypeIdentity(parameter))));
    }
}
