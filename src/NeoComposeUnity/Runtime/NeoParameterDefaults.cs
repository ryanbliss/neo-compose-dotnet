// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P65 §2.5 — callee-side completion of omitted trailing arguments from a
    /// callable's CURRENT parameter defaults, mirroring the TS evaluator's
    /// <c>fillTrailingParameterDefaults</c>/<c>parameterDefaultRuntimeValue</c>.
    /// Compiled call IR records only what the author wrote; filling from the
    /// callee record is what makes a default edit take effect everywhere on
    /// the next evaluation without recompiling dependents.
    /// </summary>
    internal static class NeoParameterDefaults
    {
        /// <summary>
        /// Whether the parameter declares a default. Absence of the wrapper is
        /// the only spelling of "no default": <c>{ value: null }</c> is an
        /// explicit null default (P65 §3.1).
        /// </summary>
        internal static bool HasDefault(FunctionArgumentTypeInfo argument)
        {
            return argument.defaultValue is not null;
        }

        /// <summary>Whether any parameter declares a default.</summary>
        internal static bool HasAnyDefault(FunctionArgumentTypeInfo[] argumentTypes)
        {
            foreach (FunctionArgumentTypeInfo argument in argumentTypes)
            {
                if (HasDefault(argument)) return true;
            }
            return false;
        }

        /// <summary>
        /// The call's minimum arity — placement (P65 §1.3) forces defaulted
        /// parameters to the tail, so the non-defaulted count is the shortest
        /// legal positional call.
        /// </summary>
        internal static int NonDefaultedCount(FunctionArgumentTypeInfo[] argumentTypes)
        {
            int count = 0;
            foreach (FunctionArgumentTypeInfo argument in argumentTypes)
            {
                if (!HasDefault(argument)) count++;
            }
            return count;
        }

        /// <summary>
        /// P65 §2.5 positional fill: a call may omit trailing defaulted
        /// parameters, and the omitted slots are completed from the callee's
        /// current argument types. Below the non-defaulted minimum and above
        /// the full arity remain hard errors, so a genuinely stale call still
        /// fails closed.
        /// </summary>
        internal static object?[] FillTrailingDefaults(
            object?[] args,
            FunctionArgumentTypeInfo[] argumentTypes,
            string subject)
        {
            int maxArity = argumentTypes.Length;
            int minArity = NonDefaultedCount(argumentTypes);
            string expectedArity = minArity == maxArity
                ? $"{maxArity} arguments"
                : $"between {minArity} and {maxArity} arguments";
            if (args.Length > maxArity)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} expects {expectedArity} but received {args.Length}; " +
                    "compiled call IR or caller is stale/corrupt.");
            }
            if (args.Length < minArity)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} expects {expectedArity} but received {args.Length}; " +
                    "compiled call IR or caller is stale/corrupt.");
            }
            if (args.Length == maxArity) return args;
            var filled = new object?[maxArity];
            Array.Copy(args, filled, args.Length);
            for (int index = args.Length; index < maxArity; index++)
            {
                filled[index] = DefaultRuntimeValue(argumentTypes[index], subject);
            }
            return filled;
        }

        /// <summary>
        /// Converts a stored parameter default (P65 §3.1) to the runtime value
        /// the way a member literal default of the same kind materializes: the
        /// enum payload's single option id becomes the id-array runtime enum
        /// shape, a decimal's canonical string passes through, the remaining
        /// constant kinds are their payload verbatim, and the explicit null
        /// default fills null.
        /// </summary>
        internal static object? DefaultRuntimeValue(
            FunctionArgumentTypeInfo parameter,
            string subject)
        {
            ParameterDefaultValue? defaultValue = parameter.defaultValue;
            if (defaultValue is null)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} parameter '{parameter.name}' was omitted but declares no default.");
            }
            if (defaultValue.value is null) return null;
            if (parameter.type != MemberKind.Enum) return defaultValue.value;
            if (defaultValue.value is string optionId)
            {
                return new[] { optionId };
            }
            throw new NSGetterRuntimeError(
                $"{subject} parameter '{parameter.name}' declares an enum default that is not an option id.");
        }
    }
}
