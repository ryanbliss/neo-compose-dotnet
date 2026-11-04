// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>An out slot used by generated function and native-handler forwarders.</summary>
    public sealed class NeoOutArgument
    {
        public object? Value
        {
            get; set;
        }
        internal NeoOutReference? NativeTarget;

        internal sealed class Binding
        {
            internal readonly NeoOutArgument Host;
            internal readonly NeoOutReference Reference = NeoOutReference.Discard();
            internal readonly TypeInfo Type;
            internal Binding(NeoOutArgument host, TypeInfo type)
            {
                Host = host;
                Type = type;
            }
        }

        internal static List<Binding>? Bind(ref object?[] args, FunctionArgumentTypeInfo[] parameters)
        {
            List<Binding>? bindings = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is not NeoOutArgument host)
                    continue;
                if (i >= parameters.Length || parameters[i].mode != NeoParameterModeKind.Out)
                    throw new NSGetterRuntimeError($"Argument {i + 1} does not accept an out argument.");
                if (bindings is null)
                {
                    bindings = new List<Binding>();
                    args = (object?[])args.Clone();
                }
                var binding = new Binding(host, parameters[i]);
                bindings.Add(binding);
                args[i] = binding.Reference;
            }
            return bindings;
        }

        internal static void Complete(List<Binding>? bindings, NSGetterEvaluator.Context ctx)
        {
            if (bindings is null)
                return;
            // Convert all outputs before publishing any, so a failed crossing
            // leaves every host out cell untouched.
            var values = new object?[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
                values[i] = NeoGeneratedTypesSupport.GeneratedResult(bindings[i].Reference.Read(), NSGetterEvaluator.CloseContextType(bindings[i].Type, ctx), ctx);
            for (int i = 0; i < bindings.Count; i++)
                bindings[i].Host.Value = values[i];
        }
    }
}
