// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSPointer</c> discriminated
    /// union. See the subclasses below for each concrete variant.
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
        public virtual string? callSiteId
        {
            get; set;
        }
    }

    /// <summary>Mirror of <c>INSPointerReference</c>.</summary>
    public sealed class ReferencePointer : Pointer
    {
        public string valueId = null!;

        /// <summary>
        /// When true, resolve <see cref="valueId"/> as an authored source id
        /// within the lexical receiver's cloned ownership graph. Absent and
        /// false retain exact-id reference semantics.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? withProvenance;
    }

    /// <summary>Mirror of <c>INSPointerVariable</c>.</summary>
    public sealed class VariablePointer : Pointer
    {
        [Newtonsoft.Json.JsonIgnore] internal NeoScript.NeoScriptVariableBinding? runtimeBinding;
        // Layouts a read from this pointer walked through that do not declare it.
        [Newtonsoft.Json.JsonIgnore] internal NeoScript.NeoScriptScopeLayout[]? absentLayouts;
        public string variableId = null!;
    }

    /// <summary>
    /// Mirror of <c>INSPointerValue</c> — wraps an
    /// <see cref="Export.Value"/> literal expression.
    /// </summary>
    public sealed class ValuePointer : Pointer
    {
        public Value value = null!;
        // Evaluator cache for a primitive literal, or for the entries of an
        // array literal of primitives (see NSGetterEvaluator).
        internal bool primitiveResolved;
        internal object? primitive;
        internal object?[]? primitiveEntries;
        // Evaluator cache for an array literal read only by a comparison.
        internal object? comparand;
    }

    /// <summary>Mirror of <c>INSPointerOperation</c>.</summary>
    public sealed class OperationPointer : Pointer
    {
        public Operation operation = null!;
    }

    /// <summary>Mirror of <c>INSPointerFunction</c>.</summary>
    public sealed class FunctionPointer : Pointer
    {
        public Function function = null!;
    }

    /// <summary>
    /// Class-owned stored member pointer. The stable member id is resolved
    /// against authored/Save/Session binding state at evaluation time.
    /// </summary>
    public sealed class StaticMemberPointer : Pointer
    {
        public string memberId = null!;
    }

    /// <summary>
    /// P67 §6 — mirror of <c>INSPointerVariant</c>. <see cref="variantId"/> is
    /// nullable by contract, not by omission: null is the reserved base
    /// selection (`&lt;Class&gt;.Variants.Base`), meaning the class itself with
    /// no variant applied.
    /// </summary>
    public sealed class VariantPointer : Pointer
    {
        public string classId = null!;
        public string? variantId;
        /// <summary>P68 §6 — a row bound by a plain Variant member.</summary>
        public string? rowValueId;
    }

    /// <summary>Mirror of <c>INSPointerKeyOf</c>.</summary>
    public sealed class KeyOfPointer : Pointer
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
    public sealed class ListLiteralPointer : Pointer
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
    public sealed class DictLiteralPointer : Pointer
    {
        public CollectionTypeInfo typeInfo = null!;
        public DictLiteralPair[] entries = null!;
    }

    /// <summary>Mirror of <c>INSPointerForceUnwrap</c>.</summary>
    public sealed class ForceUnwrapPointer : Pointer
    {
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSPointerIsCheck</c>.</summary>
    public sealed class IsCheckPointer : Pointer
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

        // Every call and getter read asks; a string compare each time
        // showed in the interpreter's profile. Keyed by the kind string
        // itself, so reassigning kind recomputes.
        [JsonIgnore] private string? isStaticKind;
        [JsonIgnore] private bool isStatic;

        [JsonIgnore]
        public bool IsStatic
        {
            get
            {
                if (!ReferenceEquals(isStaticKind, kind))
                {
                    isStatic = kind == CallReceiverKind.Static;
                    isStaticKind = kind;
                }
                return isStatic;
            }
        }
    }

    /// <summary>Mirror of <c>INSPointerCallGetter</c>.</summary>
    public sealed class CallGetterPointer : Pointer, ISchemaResolutionSite
    {
        public string memberId = null!;
        public CallReceiver receiver = null!;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? dispatch;
        /// <summary>
        /// `true` when the source used `?.` chaining. TS field is
        /// <c>optional?: boolean</c> — absent on the wire when not
        /// authored. Nullable here; `null` is functionally equivalent
        /// to `false`.
        /// </summary>
        public bool? optional;
        /// <summary>The runtime's resolution of <see cref="memberId"/>'s schema placement.</summary>
        [JsonIgnore]
        internal NeoScript.NSGetterEvaluator.PlacementSite? placementSite;
        /// <summary>The runtime's schema entries for the placement's key, one per receiver Class.</summary>
        [JsonIgnore]
        internal NeoScript.NSGetterEvaluator.MemberSiteTarget? resolvedMembers;

        void ISchemaResolutionSite.ForgetResolution()
        {
            placementSite = null;
            resolvedMembers = null;
        }
    }

    /// <summary>Mirror of <c>INSPointerCoalesce</c>.</summary>
    public sealed class CoalescePointer : Pointer
    {
        public Pointer left = null!;
        public Pointer right = null!;
    }

    /// <summary>
    /// Lazy conditional pointer. Only the selected result pointer is
    /// evaluated after the normalized boolean condition.
    /// </summary>
    public sealed class ConditionalPointer : Pointer
    {
        public Pointer condition = null!;
        public Pointer whenTrue = null!;
        public Pointer whenFalse = null!;
    }

    /// <summary>Constructs once and applies ordinary assignments in order.</summary>
    public sealed class ObjectInitializerPointer : Pointer
    {
        public Variable receiver = null!;
        public AssignInstruction[] assignments = null!;
    }

    /// <summary>
    /// Constructs a NeoDelegate closure. Capture pointers bind the trailing parameters.
    /// </summary>
    public sealed class DelegateClosurePointer : Pointer
    {
        public DelegateTypeInfo typeInfo = null!;
        public FunctionWithReturnType action = null!;
        public Pointer[] captures = null!;
        public string? code;
    }

    /// <summary>Mirror of <c>INSPointerToBool</c>.</summary>
    public sealed class ToBoolPointer : Pointer
    {
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSPointerStringify</c>.</summary>
    public sealed class StringifyPointer : Pointer
    {
        public Pointer pointer = null!;
        public TypeInfo sourceType = null!;
    }

    /// <summary>Changes a placed tile's class without constructing a target.</summary>
    [JsonConverter(typeof(PointerConverter))]
    public sealed class TileConvertPointer : Pointer
    {
        public Pointer receiverPointer = null!;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? targetClassId;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Pointer? targetPointer;
        public override string? callSiteId
        {
            get; set;
        }
    }

    /// <summary>
    /// General native-or-NeoScript callable pointer introduced by export
    /// the current callable contract. Exactly one of <see cref="memberId"/> and
    /// <see cref="memberKey"/> identifies the call target.
    /// </summary>
    [JsonConverter(typeof(PointerConverter))]
    public sealed class CallFunctionPointer : Pointer, ISchemaResolutionSite
    {
        public string? memberId;
        public string? memberKey;
        public CallReceiver receiver = null!;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? dispatch;
        public Pointer[] args = null!;
        public bool? optional;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? missingMemberFallback;
        public override string? callSiteId
        {
            get; set;
        }
        /// <summary>The runtime's resolutions of this call site, one per receiver Class.</summary>
        [JsonIgnore]
        internal NeoScript.NSGetterEvaluator.CallSiteTarget? resolvedTargets;
        /// <summary>The site's last target its chain could not keep.</summary>
        [JsonIgnore]
        internal NeoScript.NSGetterEvaluator.CallSiteTarget? uncachedTarget;

        void ISchemaResolutionSite.ForgetResolution()
        {
            resolvedTargets = null;
            uncachedTarget = null;
        }
        /// <summary>This call site's argument buffer; see <see cref="NeoScript.NSGetterEvaluator.RentArguments"/>.</summary>
        [JsonIgnore]
        internal object?[]? argumentBuffer;
        /// <summary>1 while a call holds <see cref="argumentBuffer"/>.</summary>
        [JsonIgnore]
        internal int argumentBufferInUse;
    }

    /// <summary>
    /// Invokes a delegate value resolved at runtime. Unlike
    /// <see cref="CallFunctionPointer"/>, the callable itself is a pointer.
    /// </summary>
    public sealed class CallDelegatePointer : Pointer
    {
        public Pointer @delegate = null!;
        public Pointer[] args = null!;
        public bool? optional;
        public override string? callSiteId
        {
            get; set;
        }
    }

    /// <summary>
    /// Fires every listener of an NSAction value (P62 §3.1). Mirrors
    /// <c>INSPointerCallAction</c>. Unlike <see cref="CallDelegatePointer"/>
    /// there is no <c>optional</c> field: an action is never nullable, so
    /// <c>?.</c> invocation cannot arise, and an empty listener set is a
    /// successful no-op rather than a null-target throw.
    /// </summary>
    public sealed class CallActionPointer : Pointer
    {
        public Pointer action = null!;
        public Pointer[] args = null!;
        public override string? callSiteId
        {
            get; set;
        }
    }

    public sealed class FunctionErrorCheckPointer : Pointer
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
                case PointerKind.Reference:
                    return typeof(ReferencePointer);
                case PointerKind.Variable:
                    return typeof(VariablePointer);
                case PointerKind.Value:
                    return typeof(ValuePointer);
                case PointerKind.Operation:
                    return typeof(OperationPointer);
                case PointerKind.Function:
                    return typeof(FunctionPointer);
                case PointerKind.KeyOf:
                    return typeof(KeyOfPointer);
                case PointerKind.ListLiteral:
                    return typeof(ListLiteralPointer);
                case PointerKind.DictLiteral:
                    return typeof(DictLiteralPointer);
                case PointerKind.ForceUnwrap:
                    return typeof(ForceUnwrapPointer);
                case PointerKind.IsCheck:
                    return typeof(IsCheckPointer);
                case PointerKind.CallGetter:
                    return typeof(CallGetterPointer);
                case PointerKind.Coalesce:
                    return typeof(CoalescePointer);
                case PointerKind.Conditional:
                    return typeof(ConditionalPointer);
                case PointerKind.ObjectInitializer:
                    return typeof(ObjectInitializerPointer);
                case PointerKind.DelegateClosure:
                    return typeof(DelegateClosurePointer);
                case PointerKind.ToBool:
                    return typeof(ToBoolPointer);
                case PointerKind.Stringify:
                    return typeof(StringifyPointer);
                case PointerKind.CallFunction:
                    return typeof(CallFunctionPointer);
                case PointerKind.TileConvert:
                    return typeof(TileConvertPointer);
                case PointerKind.CallDelegate:
                    return typeof(CallDelegatePointer);
                case PointerKind.CallAction:
                    return typeof(CallActionPointer);
                case PointerKind.FunctionErrorCheck:
                    return typeof(FunctionErrorCheckPointer);
                case PointerKind.StaticMember:
                    return typeof(StaticMemberPointer);
                case PointerKind.Variant:
                    return typeof(VariantPointer);
                default:
                    return null;
            }
        }

        protected override void ValidateObject(JObject obj, Type concrete)
        {
            if (concrete == typeof(TileConvertPointer))
            {
                bool hasClass = obj["targetClassId"]?.Type == JTokenType.String
                    && !string.IsNullOrWhiteSpace(obj["targetClassId"]!.Value<string>());
                bool hasTarget = obj["targetPointer"]?.Type == JTokenType.Object;
                if (obj["receiverPointer"]?.Type != JTokenType.Object
                    || obj["callSiteId"]?.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(obj["callSiteId"]!.Value<string>())
                    || hasClass == hasTarget
                    || (hasClass && obj["targetPointer"] is not null)
                    || (hasTarget && obj["targetClassId"] is not null))
                    throw new JsonSerializationException(
                        "TileConvertPointer requires receiverPointer, callSiteId and exactly one targetClassId or targetPointer.");
                return;
            }
            if (concrete == typeof(ObjectInitializerPointer))
            {
                if (obj["receiver"] is not JObject receiver
                    || receiver["id"]?.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(receiver["id"]!.Value<string>())
                    || receiver["pointer"]?.Type != JTokenType.Object
                    || receiver["typeInfo"]?.Type != JTokenType.Object)
                    throw new JsonSerializationException("ObjectInitializerPointer requires a receiver variable.");
                if (obj["assignments"] is not JArray assignments)
                    throw new JsonSerializationException("ObjectInitializerPointer requires an assignments array.");
                foreach (JToken assignment in assignments)
                    if (assignment is not JObject instruction
                        || instruction["type"]?.Value<string>() != InstructionKind.Assign)
                        throw new JsonSerializationException("ObjectInitializerPointer entries must be assignment instructions.");
                return;
            }
            if (concrete == typeof(ConditionalPointer))
            {
                foreach (string field in new[] { "condition", "whenTrue", "whenFalse" })
                {
                    if (obj[field]?.Type != JTokenType.Object)
                    {
                        throw new JsonSerializationException(
                            $"ConditionalPointer must contain a '{field}' pointer.");
                    }
                }
                return;
            }
            if (concrete == typeof(DelegateClosurePointer))
            {
                if (obj["typeInfo"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer must contain delegate 'typeInfo'.");
                }
                if (obj["action"]?.Type != JTokenType.Object)
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer must contain an 'action' object.");
                }
                if (obj["captures"]?.Type != JTokenType.Array)
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer must contain a 'captures' array.");
                }
                if (obj["captures"]!.Any(capture => capture.Type != JTokenType.Object))
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer captures must be pointers.");
                }
                if (obj["code"] is JToken code && code.Type != JTokenType.String)
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer 'code' must be a string when present.");
                }
                if (obj["code"] is JToken codeValue
                    && string.IsNullOrEmpty(codeValue.Value<string>()))
                {
                    throw new JsonSerializationException(
                        "DelegateClosurePointer 'code' must be non-empty when present.");
                }
                return;
            }
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
                ValidateDispatch(obj);
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
            if (concrete != typeof(CallFunctionPointer))
                return;

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
            ValidateDispatch(obj);
            if (obj["args"]?.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer must contain an 'args' array.");
            }
            string? fallback = obj["missingMemberFallback"]?.Type == JTokenType.String
                ? obj["missingMemberFallback"]!.Value<string>()
                : null;
            if (obj.Property("missingMemberFallback") is not null
                && fallback != "valueEquality")
            {
                throw new JsonSerializationException(
                    "CallFunctionPointer 'missingMemberFallback' must be 'valueEquality' when present.");
            }
            if (fallback == "valueEquality")
            {
                if (!hasMemberKey)
                {
                    throw new JsonSerializationException(
                        "CallFunctionPointer value-equality fallback requires a memberKey call.");
                }
                if (obj["receiver"]?["kind"]?.Value<string>() != CallReceiverKind.Instance)
                {
                    throw new JsonSerializationException(
                        "CallFunctionPointer value-equality fallback requires an instance receiver.");
                }
                if (((JArray)obj["args"]!).Count != 1)
                {
                    throw new JsonSerializationException(
                        "CallFunctionPointer value-equality fallback requires exactly one argument.");
                }
            }
        }

        private static void ValidateDispatch(JObject obj)
        {
            if (obj.Property("dispatch") is null)
                return;
            if (obj["dispatch"]?.Type != JTokenType.String || obj["dispatch"]!.Value<string>() != "base")
                throw new JsonSerializationException("Callable pointer 'dispatch' must be 'base' when present.");
            if (obj["receiver"]?["kind"]?.Value<string>() != CallReceiverKind.Instance)
                throw new JsonSerializationException("Base dispatch requires an instance receiver.");
            if (!HasNonEmptyString(obj, "memberId"))
                throw new JsonSerializationException("Base dispatch requires an explicit memberId.");
            if (obj.Property("memberKey") is not null)
                throw new JsonSerializationException("Base dispatch cannot contain memberKey.");
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
