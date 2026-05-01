// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSPointer</c> discriminated
    /// union. 14 concrete variants — see the subclasses below for each.
    /// Newtonsoft dispatches on the string <see cref="type"/> via
    /// {@link PointerConverter}.
    /// </summary>
    [JsonConverter(typeof(PointerConverter))]
    public abstract class Pointer
    {
        /// <summary>One of <see cref="PointerKind"/>.</summary>
        public string type;
    }

    /// <summary>Mirror of <c>INSPointerReference</c>.</summary>
    public class ReferencePointer : Pointer
    {
        public string valueId;
    }

    /// <summary>Mirror of <c>INSPointerVariable</c>.</summary>
    public class VariablePointer : Pointer
    {
        public string variableId;
    }

    /// <summary>
    /// Mirror of <c>INSPointerValue</c> — wraps an
    /// <see cref="Export.Value"/> literal expression.
    /// </summary>
    public class ValuePointer : Pointer
    {
        public Value value;
    }

    /// <summary>Mirror of <c>INSPointerOperation</c>.</summary>
    public class OperationPointer : Pointer
    {
        public Operation operation;
    }

    /// <summary>Mirror of <c>INSPointerFunction</c>.</summary>
    public class FunctionPointer : Pointer
    {
        public Function function;
    }

    /// <summary>Mirror of <c>INSPointerKeyOf</c>.</summary>
    public class KeyOfPointer : Pointer
    {
        public KeyOf keyOf;
        /// <summary>
        /// `true` when the source used optional chaining (`?.` /
        /// `?.[i]`). Defaults to <c>false</c>; absent on the wire when
        /// not authored.
        /// </summary>
        public bool optional;
    }

    /// <summary>
    /// Mirror of <c>INSPointerListLiteral</c>. The TS-side variant uses
    /// the field name <c>entries</c> for the list of entry pointers;
    /// kept verbatim here.
    /// </summary>
    public class ListLiteralPointer : Pointer
    {
        public CollectionTypeInfo typeInfo;
        public Pointer[] entries;
    }

    /// <summary>
    /// Mirror of <c>INSPointerDictLiteral</c>. The TS-side variant uses
    /// the field name <c>entries</c> for the list of {key, value}
    /// pairs; kept verbatim here. Per-variant deserialization (driven
    /// by the <see cref="Pointer.type"/> discriminator) means the
    /// <c>entries</c> field shape is unambiguous despite the name
    /// collision with {@link ListLiteralPointer}.
    /// </summary>
    public class DictLiteralPointer : Pointer
    {
        public CollectionTypeInfo typeInfo;
        public DictLiteralPair[] entries;
    }

    /// <summary>Mirror of <c>INSPointerForceUnwrap</c>.</summary>
    public class ForceUnwrapPointer : Pointer
    {
        public Pointer pointer;
    }

    /// <summary>Mirror of <c>INSPointerIsCheck</c>.</summary>
    public class IsCheckPointer : Pointer
    {
        public Pointer pointer;
        public TypeInfo checkType;
    }

    /// <summary>Mirror of <c>INSPointerCallGetter</c>.</summary>
    public class CallGetterPointer : Pointer
    {
        public string attributeId;
        public Pointer thisPointer;
        public bool optional;
    }

    /// <summary>Mirror of <c>INSPointerCoalesce</c>.</summary>
    public class CoalescePointer : Pointer
    {
        public Pointer left;
        public Pointer right;
    }

    /// <summary>Mirror of <c>INSPointerToBool</c>.</summary>
    public class ToBoolPointer : Pointer
    {
        public Pointer pointer;
    }

    /// <summary>Mirror of <c>INSPointerStringify</c>.</summary>
    public class StringifyPointer : Pointer
    {
        public Pointer pointer;
        public TypeInfo sourceType;
    }

    public class PointerConverter : DiscriminatedConverter<Pointer>
    {
        protected override Type ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case PointerKind.Reference: return typeof(ReferencePointer);
                case PointerKind.Variable: return typeof(VariablePointer);
                case PointerKind.Value: return typeof(ValuePointer);
                case PointerKind.Operation: return typeof(OperationPointer);
                case PointerKind.Function: return typeof(FunctionPointer);
                case PointerKind.KeyOf: return typeof(KeyOfPointer);
                case PointerKind.ListLiteral: return typeof(ListLiteralPointer);
                case PointerKind.DictLiteral: return typeof(DictLiteralPointer);
                case PointerKind.ForceUnwrap: return typeof(ForceUnwrapPointer);
                case PointerKind.IsCheck: return typeof(IsCheckPointer);
                case PointerKind.CallGetter: return typeof(CallGetterPointer);
                case PointerKind.Coalesce: return typeof(CoalescePointer);
                case PointerKind.ToBool: return typeof(ToBoolPointer);
                case PointerKind.Stringify: return typeof(StringifyPointer);
                default: return null;
            }
        }
    }
}
