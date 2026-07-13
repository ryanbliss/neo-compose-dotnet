// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

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
        public string type = null!;
    }

    /// <summary>Mirror of <c>INSPointerReference</c>.</summary>
    public class ReferencePointer : Pointer
    {
        public string valueId = null!;
    }

    /// <summary>Mirror of <c>INSPointerVariable</c>.</summary>
    public class VariablePointer : Pointer
    {
        public string variableId = null!;
    }

    /// <summary>
    /// Mirror of <c>INSPointerValue</c> — wraps an
    /// <see cref="Export.Value"/> literal expression.
    /// </summary>
    public class ValuePointer : Pointer
    {
        public Value value = null!;
    }

    /// <summary>Mirror of <c>INSPointerOperation</c>.</summary>
    public class OperationPointer : Pointer
    {
        public Operation operation = null!;
    }

    /// <summary>Mirror of <c>INSPointerFunction</c>.</summary>
    public class FunctionPointer : Pointer
    {
        public Function function = null!;
    }

    /// <summary>Mirror of <c>INSPointerKeyOf</c>.</summary>
    public class KeyOfPointer : Pointer
    {
        public KeyOf keyOf = null!;
        /// <summary>
        /// `true` when the source used optional chaining (`?.` /
        /// `?.[i]`). TS field is <c>optional?: boolean</c> — absent on
        /// the wire when not authored. Nullable here so callers can
        /// distinguish "explicitly false" from "absent" if needed;
        /// `null` is functionally equivalent to `false` for the
        /// evaluator.
        /// </summary>
        public bool? optional;
    }

    /// <summary>
    /// Mirror of <c>INSPointerListLiteral</c>. The TS-side variant uses
    /// the field name <c>entries</c> for the list of entry pointers;
    /// kept verbatim here.
    /// </summary>
    public class ListLiteralPointer : Pointer
    {
        public CollectionTypeInfo typeInfo = null!;
        public Pointer[] entries = null!;
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
        public CollectionTypeInfo typeInfo = null!;
        public DictLiteralPair[] entries = null!;
    }

    /// <summary>Mirror of <c>INSPointerForceUnwrap</c>.</summary>
    public class ForceUnwrapPointer : Pointer
    {
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSPointerIsCheck</c>.</summary>
    public class IsCheckPointer : Pointer
    {
        public Pointer pointer = null!;
        public TypeInfo checkType = null!;
    }

    /// <summary>Mirror of <c>INSPointerCallGetter</c>.</summary>
    public class CallGetterPointer : Pointer
    {
        public string attributeId = null!;
        public Pointer thisPointer = null!;
        /// <summary>
        /// `true` when the source used `?.` chaining. TS field is
        /// <c>optional?: boolean</c> — absent on the wire when not
        /// authored. Nullable here; `null` is functionally equivalent
        /// to `false`.
        /// </summary>
        public bool? optional;
    }

    /// <summary>Mirror of <c>INSPointerCoalesce</c>.</summary>
    public class CoalescePointer : Pointer
    {
        public Pointer left = null!;
        public Pointer right = null!;
    }

    /// <summary>Mirror of <c>INSPointerToBool</c>.</summary>
    public class ToBoolPointer : Pointer
    {
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSPointerStringify</c>.</summary>
    public class StringifyPointer : Pointer
    {
        public Pointer pointer = null!;
        public TypeInfo sourceType = null!;
    }

    /// <summary>
    /// General native-or-NeoScript callable pointer introduced by export
    /// schema 6. Exactly one of <see cref="attributeId"/> and
    /// <see cref="memberKey"/> identifies the call target.
    /// </summary>
    [JsonConverter(typeof(PointerConverter))]
    public class CallFunctionPointer : Pointer
    {
        public string? attributeId;
        public string? memberKey;
        public Pointer thisPointer = null!;
        public Pointer[] args = null!;
        public bool? optional;
        public string callSiteId = null!;
    }

    public class FunctionErrorCheckPointer : Pointer
    {
        public CallFunctionPointer call = null!;
        public string mode = null!;
    }

    public class PointerConverter : DiscriminatedConverter<Pointer>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
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
                case PointerKind.CallFunction: return typeof(CallFunctionPointer);
                case PointerKind.FunctionErrorCheck: return typeof(FunctionErrorCheckPointer);
                default: return null;
            }
        }

        protected override void ValidateObject(JObject obj, Type concrete)
        {
            if (concrete == typeof(FunctionErrorCheckPointer))
            {
                if (obj["call"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "FunctionErrorCheckPointer must contain a 'call' object.");
                }
                string? mode = obj["mode"]?.Type == JTokenType.String
                    ? obj["mode"]!.Value<string>()
                    : null;
                if (mode != FunctionErrorCheckKind.Throws
                    && mode != FunctionErrorCheckKind.DoesNotThrow)
                {
                    throw new JsonSerializationException(
                        "FunctionErrorCheckPointer 'mode' must be 'throws' or 'doesNotThrow'.");
                }
                return;
            }
            if (concrete != typeof(CallFunctionPointer)) return;

            bool hasAttributeId = HasNonEmptyString(obj, "attributeId");
            bool hasMemberKey = HasNonEmptyString(obj, "memberKey");
            if (hasAttributeId == hasMemberKey)
            {
                throw new JsonSerializationException(
                    hasAttributeId
                        ? "CallFunctionPointer cannot contain both 'attributeId' and 'memberKey'."
                        : "CallFunctionPointer must contain either 'attributeId' or 'memberKey'.");
            }
            if (!HasNonEmptyString(obj, "callSiteId"))
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain a non-empty 'callSiteId'.");
            }
            if (obj["thisPointer"]?.Type != JTokenType.Object)
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain a 'thisPointer' object.");
            }
            if (obj["args"]?.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain an 'args' array.");
            }
        }

        private static bool HasNonEmptyString(JObject obj, string propertyName)
        {
            var token = obj[propertyName];
            return token?.Type == JTokenType.String
                && !string.IsNullOrEmpty(token.Value<string>());
        }
    }
}
