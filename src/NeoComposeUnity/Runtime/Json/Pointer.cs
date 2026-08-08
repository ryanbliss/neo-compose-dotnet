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

        /// <summary>
        /// Stable source location for call pointers. Kept on the base shape so
        /// instruction consumers can resume either a statically resolved
        /// function call or a runtime delegate call without narrowing first.
        /// Non-call pointers leave it null and omit it from JSON.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public virtual string? callSiteId { get; set; }
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

    /// <summary>
    /// Class-owned stored member pointer. The stable member id is resolved
    /// against authored/Save/Session binding state at evaluation time.
    /// </summary>
    public class StaticMemberPointer : Pointer
    {
        public string memberId = null!;
    }

    /// <summary>Mirror of <c>INSPointerKeyOf</c>.</summary>
    public class KeyOfPointer : Pointer
    {
        public KeyOf keyOf = null!;
        /// <summary>
        /// Optional stable schema-member identity for a statically resolved
        /// Class/interface field read. Schema 11 emitters place this beside
        /// <see cref="keyOf"/> on the pointer variant, not inside the keyOf
        /// operand payload.
        /// </summary>
        public string? memberId;

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

    public static class CallReceiverKind
    {
        public const string Instance = "instance";
        public const string Static = "static";
    }

    /// <summary>
    /// Explicit receiver union shared by callGetter/callFunction. Instance
    /// calls carry a pointer; receiverless class-owned calls carry the stable
    /// callable member id. A null/fake this pointer is never used to encode
    /// static dispatch.
    /// </summary>
    public class CallReceiver
    {
        public string kind = null!;
        public Pointer? pointer;
        public string? memberId;

        public static CallReceiver Instance(Pointer pointer) => new()
        {
            kind = CallReceiverKind.Instance,
            pointer = pointer ?? throw new ArgumentNullException(nameof(pointer)),
        };

        public static CallReceiver Static(string memberId) => new()
        {
            kind = CallReceiverKind.Static,
            memberId = string.IsNullOrEmpty(memberId)
                ? throw new ArgumentException("A static call receiver requires a member id.", nameof(memberId))
                : memberId,
        };

        [JsonIgnore]
        public bool IsStatic => kind == CallReceiverKind.Static;
    }

    /// <summary>Mirror of <c>INSPointerCallGetter</c>.</summary>
    public class CallGetterPointer : Pointer
    {
        public string memberId = null!;
        public CallReceiver receiver = null!;
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
    /// the current callable contract. Exactly one of <see cref="memberId"/> and
    /// <see cref="memberKey"/> identifies the call target.
    /// </summary>
    [JsonConverter(typeof(PointerConverter))]
    public class CallFunctionPointer : Pointer
    {
        public string? memberId;
        public string? memberKey;
        public CallReceiver receiver = null!;
        public Pointer[] args = null!;
        public bool? optional;
        public override string? callSiteId { get; set; }
    }

    /// <summary>
    /// Invokes a delegate value resolved at runtime. Unlike
    /// <see cref="CallFunctionPointer"/>, the callable itself is a pointer.
    /// </summary>
    public class CallDelegatePointer : Pointer
    {
        public Pointer @delegate = null!;
        public Pointer[] args = null!;
        public bool? optional;
        public override string? callSiteId { get; set; }
    }

    /// <summary>
    /// Fires every listener of an NSAction value (P62 §3.1). Mirrors
    /// <c>INSPointerCallAction</c>. Unlike <see cref="CallDelegatePointer"/>
    /// there is no <c>optional</c> field: an action is never nullable, so
    /// <c>?.</c> invocation cannot arise, and an empty listener set is a
    /// successful no-op rather than a null-target throw.
    /// </summary>
    public class CallActionPointer : Pointer
    {
        public Pointer action = null!;
        public Pointer[] args = null!;
        public override string? callSiteId { get; set; }
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
                case PointerKind.CallDelegate: return typeof(CallDelegatePointer);
                case PointerKind.CallAction: return typeof(CallActionPointer);
                case PointerKind.FunctionErrorCheck: return typeof(FunctionErrorCheckPointer);
                case PointerKind.StaticMember: return typeof(StaticMemberPointer);
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
            if (concrete == typeof(CallGetterPointer))
            {
                ValidateReceiver(obj);
                return;
            }
            if (concrete == typeof(CallDelegatePointer))
            {
                if (obj["delegate"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "CallDelegatePointer must contain a 'delegate' pointer.");
                }
                if (obj["args"]?.Type != JTokenType.Array)
                {
                    throw new JsonSerializationException(
                        "CallDelegatePointer must contain an 'args' array.");
                }
                if (!HasNonEmptyString(obj, "callSiteId"))
                {
                    throw new JsonSerializationException(
                        "CallDelegatePointer must contain a non-empty 'callSiteId'.");
                }
                return;
            }
            if (concrete == typeof(CallActionPointer))
            {
                if (obj["action"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "CallActionPointer must contain an 'action' pointer.");
                }
                if (obj["args"]?.Type != JTokenType.Array)
                {
                    throw new JsonSerializationException(
                        "CallActionPointer must contain an 'args' array.");
                }
                if (!HasNonEmptyString(obj, "callSiteId"))
                {
                    throw new JsonSerializationException(
                        "CallActionPointer must contain a non-empty 'callSiteId'.");
                }
                if (obj.Property("optional") is not null)
                {
                    throw new JsonSerializationException(
                        "CallActionPointer cannot contain 'optional'; an NSAction is never nullable.");
                }
                return;
            }
            if (concrete != typeof(CallFunctionPointer)) return;

            bool hasMemberId = HasNonEmptyString(obj, "memberId");
            bool hasMemberKey = HasNonEmptyString(obj, "memberKey");
            if (hasMemberId == hasMemberKey)
            {
                throw new JsonSerializationException(
                    hasMemberId
                        ? "CallFunctionPointer cannot contain both 'memberId' and 'memberKey'."
                        : "CallFunctionPointer must contain either 'memberId' or 'memberKey'.");
            }
            if (!HasNonEmptyString(obj, "callSiteId"))
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain a non-empty 'callSiteId'.");
            }
            ValidateReceiver(obj);
            if (obj["args"]?.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain an 'args' array.");
            }
        }

        private static void ValidateReceiver(JObject obj)
        {
            if (obj["receiver"] is not JObject receiver)
            {
                throw new JsonSerializationException(
                    "Callable pointer must contain a 'receiver' object.");
            }
            string? kind = receiver["kind"]?.Type == JTokenType.String
                ? receiver["kind"]!.Value<string>()
                : null;
            if (kind == CallReceiverKind.Instance)
            {
                if (receiver["pointer"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "Instance call receiver must contain a 'pointer' object.");
                }
                if (HasNonEmptyString(receiver, "memberId"))
                {
                    throw new JsonSerializationException(
                        "Instance call receiver cannot contain 'memberId'.");
                }
                return;
            }
            if (kind == CallReceiverKind.Static)
            {
                if (!HasNonEmptyString(receiver, "memberId"))
                {
                    throw new JsonSerializationException(
                        "Static call receiver must contain a non-empty 'memberId'.");
                }
                if (receiver["pointer"] is not null)
                {
                    throw new JsonSerializationException(
                        "Static call receiver cannot contain 'pointer'.");
                }
                return;
            }
            throw new JsonSerializationException(
                "Call receiver 'kind' must be 'instance' or 'static'.");
        }

        private static bool HasNonEmptyString(JObject obj, string propertyName)
        {
            var token = obj[propertyName];
            return token?.Type == JTokenType.String
                && !string.IsNullOrEmpty(token.Value<string>());
        }
    }
}
