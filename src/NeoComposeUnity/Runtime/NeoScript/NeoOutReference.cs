// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>A transient alias to an out argument, never a storable NeoScript value.</summary>
    internal sealed class NeoOutReference
    {
        private readonly Func<object?> read;
        private readonly Action<object?> write;

        internal NeoOutReference(Func<object?> read, Action<object?> write)
        {
            this.read = read;
            this.write = write;
        }

        internal object? Read() => read();
        internal void Write(object? value) => write(value);
        internal static object? Value(object? value) => value is NeoOutReference reference ? reference.Read() : value;

        internal static NeoOutReference Discard(object? value = null)
        {
            return new NeoOutReference(() => value, assigned => value = assigned);
        }
    }

    /// <summary>Contract §10.1: constructor out arguments record only their mode.</summary>
    internal static class NeoConstructorOut
    {
        internal static JObject Marker() => new() { ["out"] = true };

        internal static bool Validate(FunctionArgumentTypeInfo parameter, JToken? token, string parameterId)
        {
            bool marker = token is JObject { Count: 1 } obj && obj["out"] is JValue { Type: JTokenType.Boolean } flag && flag.Value<bool>();
            if (parameter.mode == NeoParameterModeKind.Out)
            {
                if (!marker)
                    throw new InvalidOperationException($"Constructor out argument '{parameterId}' must be exactly {{\"out\":true}}.");
                return true;
            }
            if (token is JObject value && value.Property("out") is not null)
                throw new InvalidOperationException($"Constructor argument '{parameterId}' has an out marker, but the parameter is not out.");
            return false;
        }

        internal static NeoOutReference Replay(NeoClient client, TypeInfo closedType) =>
            NeoOutReference.Discard(NSGetterEvaluator.DefaultOf(closedType, client));
    }

}
