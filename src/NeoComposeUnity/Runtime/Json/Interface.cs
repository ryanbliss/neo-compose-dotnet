// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// One property, Function or static field declaration in a class
    /// interface. The <see cref="kind"/> discriminator determines which
    /// signature fields are present.
    /// </summary>
    [JsonConverter(typeof(InterfaceMemberConverter))]
    public class InterfaceMember
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NeoInterfaceMemberKind kind;

        /// <summary>
        /// Contract accessibility (specs/member-access-modifiers.md Decision
        /// 3): the accessibility the implementing class member must declare.
        /// Absent means Public.
        /// </summary>
        [JsonProperty("access", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberAccessKind? access;

        /// <summary>P127 §5. Present only as <c>true</c>; absent means an instance member.</summary>
        [JsonProperty("static", NullValueHandling = NullValueHandling.Ignore)]
        private bool? isStatic;
        /// <summary>P127 §5. Abstract, Virtual or Sealed; absent reads by body and staticness.</summary>
        [JsonProperty("modifier", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberModifierKind? modifier;
        /// <summary>
        /// P127 §5. The interface-owned member record holding the body or the
        /// static field's value; absent on a member with no body.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? implementationMemberId;
        /// <summary>P127 §5. The base interface member this one overrides or reabstracts.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ExplicitInterfaceImplementation? explicitImplementation;
        /// <summary>P127 §6. Present only as <c>true</c>, on a user-defined operator.</summary>
        [JsonProperty("operator", NullValueHandling = NullValueHandling.Ignore)]
        private bool? isOperator;

        // Property signature (also a static field's type).
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TypeInfo? typeInfo;
        [JsonProperty("accessors", NullValueHandling = NullValueHandling.Ignore)]
        private NeoPropertyAccessorsKind? accessors;
        /// <summary>P127 §5. Absent means the member's own <see cref="Access"/>.</summary>
        [JsonProperty("getterAccess", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberAccessKind? getterAccess;
        [JsonProperty("setterAccess", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberAccessKind? setterAccess;

        // Function signature.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo? returnTypeInfo;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<FunctionArgumentTypeInfo>? argumentTypes;
        /// <summary>P110 §4. The function's own type parameters; null when it has none.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public FunctionGenericParam[]? genericParams;
        [JsonProperty("dispatch", NullValueHandling = NullValueHandling.Ignore)]
        private NeoFunctionDispatchKind? dispatch;

        [JsonIgnore]
        public NeoMemberAccessKind Access
        {
            get => access ?? NeoMemberAccessKind.Public;
            set => access = value;
        }

        [JsonIgnore]
        public NeoPropertyAccessorsKind Accessors
        {
            get => accessors ?? NeoPropertyAccessorsKind.Get;
            set => accessors = value;
        }

        [JsonIgnore]
        public NeoFunctionDispatchKind Dispatch
        {
            get => dispatch ?? NeoFunctionDispatchKind.Synchronous;
            set => dispatch = value;
        }

        [JsonIgnore]
        public bool IsStatic
        {
            get => isStatic == true;
            set => isStatic = value ? true : null;
        }

        [JsonIgnore]
        public bool IsOperator
        {
            get => isOperator == true;
            set => isOperator = value ? true : null;
        }

        /// <summary>
        /// A property with a <c>set</c> accessor: <c>GetSet</c> or <c>Set</c>.
        /// An <c>init</c> accessor (P127 §5) isn't one.
        /// </summary>
        [JsonIgnore]
        public bool IsSettable =>
            kind == NeoInterfaceMemberKind.Property
            && (Accessors == NeoPropertyAccessorsKind.GetSet || Accessors == NeoPropertyAccessorsKind.Set);

        [JsonIgnore]
        public NeoMemberAccessKind GetterAccess
        {
            get => getterAccess ?? Access;
            set => getterAccess = value;
        }

        [JsonIgnore]
        public NeoMemberAccessKind SetterAccess
        {
            get => setterAccess ?? Access;
            set => setterAccess = value;
        }

        [JsonIgnore]
        internal NeoMemberAccessKind? DeclaredAccess => access;

        [JsonIgnore]
        internal NeoMemberModifierKind? DeclaredModifier
        {
            get => modifier;
            set => modifier = value;
        }

        [JsonIgnore]
        internal NeoMemberAccessKind? DeclaredGetterAccess => getterAccess;

        [JsonIgnore]
        internal NeoMemberAccessKind? DeclaredSetterAccess => setterAccess;

        [JsonIgnore]
        internal NeoPropertyAccessorsKind? DeclaredAccessors => accessors;

        [JsonIgnore]
        internal NeoFunctionDispatchKind? DeclaredDispatch => dispatch;
    }

    /// <summary>
    /// Class interface declaration. Mirrors the TS-side
    /// <c>INeoInterface</c> export record.
    /// </summary>
    public class Interface
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, InterfaceMember> members = null!;
        /// <summary>Existing export metadata identifying a protected system interface.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public JObject? system;
        public List<string>? memberKeyOrder;
        /// <summary>
        /// P127 §4.7. Each extended interface construction, in order. A
        /// generic one binds every param: a forward of this interface's own
        /// param, or a closed Member binding.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<InterfaceConstruction>? extendsInterfaces;
        /// <summary>P113 §2.4. Ordered params, shaped like a class's.</summary>
        public List<GenericParamDeclaration>? genericParams;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public class InterfaceMemberConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(InterfaceMember);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var json = NeoJsonObjectReader.ReadForPopulate(reader);
            RecordShapeContractGuard.ValidateInterfaceMember(json);
            var kind = StrictRecordShapeEnums.ReadDefaulted(
                json,
                "kind",
                "Interface member",
                NeoInterfaceMemberKind.Property);
            switch (kind)
            {
                case NeoInterfaceMemberKind.Property:
                    ValidateProperty(json);
                    break;
                case NeoInterfaceMemberKind.Function:
                    ValidateFunction(json);
                    break;
                case NeoInterfaceMemberKind.Field:
                    ValidateField(json);
                    break;
                default:
                    throw new JsonSerializationException(
                        $"Unknown interface member kind '{(int)kind}'.");
            }

            var member = new InterfaceMember();
            NeoJsonObjectReader.Populate(reader, json, serializer, member);
            ValidateSupportedTypeInfos(member);
            return member;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        private static void ValidateProperty(JObject json)
        {
            RequireNonNull(json, "typeInfo", "Property interface member");
            StrictRecordShapeEnums.ValidateOptional<NeoPropertyAccessorsKind>(
                json,
                "accessors",
                "Property interface member");
            RejectPresent(json, "returnTypeInfo", "Property interface member");
            RejectPresent(json, "argumentTypes", "Property interface member");
            RejectPresent(json, "dispatch", "Property interface member");
            RejectPresent(json, "operator", "Property interface member");
        }

        private static void ValidateFunction(JObject json)
        {
            RequireNonNull(json, "returnTypeInfo", "Function interface member");
            if (json["argumentTypes"]?.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    "Function interface member requires an 'argumentTypes' array.");
            }
            StrictRecordShapeEnums.ValidateOptional<NeoFunctionDispatchKind>(
                json,
                "dispatch",
                "Function interface member");
            RejectPresent(json, "typeInfo", "Function interface member");
            RejectPresent(json, "accessors", "Function interface member");
            RejectPresent(json, "getterAccess", "Function interface member");
            RejectPresent(json, "setterAccess", "Function interface member");
        }

        /// <summary>P127 §5. A static interface field: a type and a value-holding implementation.</summary>
        private static void ValidateField(JObject json)
        {
            RequireNonNull(json, "typeInfo", "Field interface member");
            if (json["static"]?.Type != JTokenType.Boolean || !json.Value<bool>("static"))
            {
                throw new JsonSerializationException(
                    "Field interface member requires 'static: true'.");
            }
            foreach (string field in new[] { "accessors", "getterAccess", "setterAccess", "returnTypeInfo", "argumentTypes", "genericParams", "dispatch", "operator" })
            {
                RejectPresent(json, field, "Field interface member");
            }
        }

        private static void RequireNonNull(
            JObject json,
            string propertyName,
            string subject)
        {
            if (json[propertyName] is null || json[propertyName]!.Type == JTokenType.Null)
            {
                throw new JsonSerializationException(
                    $"{subject} requires '{propertyName}'.");
            }
        }

        private static void RejectPresent(
            JObject json,
            string propertyName,
            string subject)
        {
            if (json[propertyName] is not null)
            {
                throw new JsonSerializationException(
                    $"{subject} cannot contain '{propertyName}'.");
            }
        }

        private static void ValidateSupportedTypeInfos(InterfaceMember member)
        {
            if (member.kind == NeoInterfaceMemberKind.Property || member.kind == NeoInterfaceMemberKind.Field)
            {
                RejectUnsupportedTypeInfo(member.typeInfo!, $"{member.kind} interface member");
                return;
            }

            RejectUnsupportedTypeInfo(member.returnTypeInfo!, "Function interface member return");
            foreach (var argument in member.argumentTypes!)
            {
                RejectUnsupportedTypeInfo(argument, "Function interface member argument");
            }
        }

        private static string? InterfaceParamOwner(TypeInfo typeInfo) => typeInfo switch
        {
            GenericTypeInfo generic => generic.ownerInterfaceId,
            FunctionArgumentTypeInfo argument => argument.ownerInterfaceId,
            _ => null,
        };

        private static void RejectUnsupportedTypeInfo(TypeInfo typeInfo, string subject)
        {
            // A class's type parameter can't appear in an interface; the
            // interface's own (P113 §2.4) and the function's own (P110 §4) can.
            if (typeInfo.type == MemberKind.Generic
                && !NeoScript.NeoFunctionTypeArguments.IsFunctionGeneric(typeInfo)
                && InterfaceParamOwner(typeInfo) is null)
            {
                throw new JsonSerializationException(
                    $"{subject} cannot use a class's type parameter.");
            }
            if (typeInfo.type == MemberKind.Unknown)
            {
                throw new JsonSerializationException(
                    $"{subject} cannot use type '{typeInfo.type}'.");
            }
            if (typeInfo.type == MemberKind.Interface)
            {
                string? interfaceId = typeInfo switch
                {
                    InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
                    FunctionArgumentTypeInfo argumentType => argumentType.interfaceId,
                    _ => null,
                };
                if (string.IsNullOrEmpty(interfaceId))
                {
                    throw new JsonSerializationException(
                        $"{subject} interface type is missing 'interfaceId'.");
                }
            }
            if (typeInfo is CollectionTypeInfo collection)
            {
                if (collection.entryTypeInfo is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} collection type is missing 'entryTypeInfo'.");
                }
                RejectUnsupportedTypeInfo(collection.entryTypeInfo, subject);
            }
            if (typeInfo is FunctionArgumentTypeInfo argument
                && argument.entryTypeInfo is not null)
            {
                RejectUnsupportedTypeInfo(argument.entryTypeInfo, subject);
            }
            if (typeInfo is DelegateTypeInfo delegateType)
            {
                if (delegateType.returnTypeInfo is null
                    || delegateType.argumentTypes is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} delegate type is missing its signature.");
                }
                RejectUnsupportedTypeInfo(delegateType.returnTypeInfo, subject);
                foreach (TypeInfo argumentType in delegateType.argumentTypes)
                {
                    RejectUnsupportedTypeInfo(argumentType, subject);
                }
            }
            if (typeInfo is ActionTypeInfo actionType)
            {
                // An action has no return slot — void-ness is structural
                // (P62 §2.1) — so only the argument list is required.
                if (actionType.argumentTypes is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} action type is missing its signature.");
                }
                foreach (TypeInfo argumentType in actionType.argumentTypes)
                {
                    RejectUnsupportedTypeInfo(argumentType, subject);
                }
            }
            if (typeInfo is FunctionArgumentTypeInfo actionArgument
                && actionArgument.type == MemberKind.NSAction)
            {
                if (actionArgument.argumentTypes is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} action type is missing its signature.");
                }
                foreach (TypeInfo argumentType in actionArgument.argumentTypes)
                {
                    RejectUnsupportedTypeInfo(argumentType, subject);
                }
            }
            if (typeInfo is FunctionArgumentTypeInfo delegateArgument
                && delegateArgument.type == MemberKind.NSDelegate)
            {
                if (delegateArgument.returnTypeInfo is null
                    || delegateArgument.argumentTypes is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} delegate type is missing its signature.");
                }
                RejectUnsupportedTypeInfo(
                    delegateArgument.returnTypeInfo,
                    subject);
                foreach (TypeInfo argumentType in delegateArgument.argumentTypes)
                {
                    RejectUnsupportedTypeInfo(argumentType, subject);
                }
            }
            if (typeInfo is ClassTypeInfo classType && classType.typeArguments is not null)
            {
                foreach (TypeInfo typeArgument in classType.typeArguments.Values)
                {
                    RejectUnsupportedTypeInfo(typeArgument, subject);
                }
            }
        }
    }
}
