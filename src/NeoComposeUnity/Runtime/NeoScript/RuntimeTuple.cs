// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>Immutable tuple slots. Referenced objects keep their own lifetimes.</summary>
    [JsonConverter(typeof(RuntimeTupleConverter))]
    internal sealed class RuntimeTuple : IReadOnlyList<object?>
    {
        private readonly object?[] elements;
        internal readonly TupleTypeInfo TypeInfo;

        internal RuntimeTuple(object?[] elements, TupleTypeInfo typeInfo)
        {
            if (elements.Length < 2)
                throw new NSGetterRuntimeError("A tuple needs at least two elements.");
            if (elements.Length != typeInfo.elements.Length)
                throw new NSGetterRuntimeError("Tuple value and type arities differ.");
            this.elements = (object?[])elements.Clone();
            TypeInfo = typeInfo;
            // Tuple slots retain the shared identity of captured lists.
            for (int i = 0; i < elements.Length; i++)
                if (typeInfo.elements[i].typeInfo.type == MemberKind.List && elements[i] is object?[] list)
                    NSGetterEvaluator.CaptureRuntimeList(list);
        }

        public int Count => elements.Length;
        public object? this[int index] => elements[index];
        public IEnumerator<object?> GetEnumerator() => ((IEnumerable<object?>)elements).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal sealed class RuntimeTupleConverter : JsonConverter
    {
        public override bool CanRead => false;
        public override bool CanConvert(Type objectType) => objectType == typeof(RuntimeTuple);
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException("Tuple captures restore through their declared type.");
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not RuntimeTuple tuple)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            writer.WritePropertyName("elements");
            writer.WriteStartArray();
            // Elements write by their own type, so a float keeps its stored form (P127 §12.1).
            for (int index = 0; index < tuple.Count; index++)
                NeoStoredNumberJsonConverter.WriteTyped(writer, tuple[index], tuple.TypeInfo.elements[index].typeInfo, serializer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

}
