// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>The arm of a structured member target. Numeric on the wire.</summary>
    public enum NeoMemberTargetKind
    {
        /// <summary>A built-in static method, which has no member row.</summary>
        BuiltIn = 0,
    }

    /// <summary>
    /// P127 §12.1: a stored delegate target naming a built-in static method
    /// (<c>int.Parse</c>, <c>decimal.Compare</c>, <c>Math.Max</c>, or a
    /// primitive's implementation reached through <c>T.Parse</c>), stored in
    /// place of a <c>memberId</c> as
    /// <c>{ kind: BuiltIn, declaring, name, arity, parameterTypes, explicitInterface?, typeArguments?, delegateType? }</c>.
    /// It names the method .NET binds. Immutable, with structural equality.
    /// </summary>
    public sealed class NeoBuiltInTarget : IEquatable<NeoBuiltInTarget>
    {
        public NeoBuiltInTarget(
            TypeBinding? declaringType,
            string? declaringClass,
            string name,
            int arity,
            TypeBinding[] parameterTypes,
            TypeBinding? explicitInterface = null,
            TypeBinding[]? typeArguments = null,
            TypeBinding? delegateType = null)
        {
            DeclaringType = declaringType;
            DeclaringClass = declaringClass;
            Name = name;
            Arity = arity;
            ParameterTypes = parameterTypes;
            ExplicitInterface = explicitInterface;
            TypeArguments = typeArguments;
            DelegateType = delegateType;
        }

        /// <summary>The declaring type's closed binding; null when <see cref="DeclaringClass"/> names a static class.</summary>
        public TypeBinding? DeclaringType
        {
            get;
        }

        /// <summary>A static built-in class's NeoScript name (<c>Math</c>); null when <see cref="DeclaringType"/> is set.</summary>
        public string? DeclaringClass
        {
            get;
        }

        public string Name
        {
            get;
        }

        /// <summary>The method's generic arity.</summary>
        public int Arity
        {
            get;
        }

        /// <summary>The method's parameter types, in which its own type parameters are <see cref="TypeBindingKind.MethodParam"/> arms.</summary>
        public TypeBinding[] ParameterTypes
        {
            get;
        }

        /// <summary>For an explicit interface implementation, the closed interface it implements.</summary>
        public TypeBinding? ExplicitInterface
        {
            get;
        }

        /// <summary>A generic method's closed type arguments, one per <see cref="Arity"/>; null for a non-generic method.</summary>
        public TypeBinding[]? TypeArguments
        {
            get;
        }

        /// <summary>The target's delegate type, when it differs from its position's declared type.</summary>
        public TypeBinding? DelegateType
        {
            get;
        }

        /// <summary>The wire keys a built-in target takes; a listener takes all but <c>delegateType</c>.</summary>
        internal static readonly string[] Keys =
        {
            "kind",
            "declaring",
            "name",
            "arity",
            "parameterTypes",
            "explicitInterface",
            "typeArguments",
            "delegateType",
        };

        /// <summary>Whether <paramref name="record"/> is the built-in arm: it carries a <c>kind</c>, which a member-id target never does.</summary>
        internal static bool IsBuiltIn(JObject record) => record.Property("kind") is not null;

        /// <summary>
        /// Reads and validates the built-in arm. <paramref name="listener"/>
        /// refuses <c>delegateType</c>, as a listener always has its
        /// action's type.
        /// </summary>
        internal static NeoBuiltInTarget Read(JObject record, string subject, bool listener)
        {
            foreach (JProperty property in record.Properties())
            {
                if (Array.IndexOf(Keys, property.Name) < 0)
                    throw new JsonSerializationException($"{subject} built-in target has '{property.Name}', which it doesn't take.");
            }
            if (record["kind"] is not JValue { Type: JTokenType.Integer } kind)
                throw new JsonSerializationException($"{subject} built-in target needs an integer 'kind'.");
            if (kind.Value<long>() != (long)NeoMemberTargetKind.BuiltIn)
                throw new JsonSerializationException($"{subject} has unknown member target kind {kind.Value<long>()}.");

            TypeBinding? declaringType = null;
            string? declaringClass = null;
            switch (record["declaring"])
            {
                case JValue { Type: JTokenType.String } named when named.Value<string>()!.Length > 0:
                    declaringClass = named.Value<string>();
                    break;
                case JObject binding:
                    declaringType = ReadIdentity(binding, $"{subject}.declaring");
                    break;
                default:
                    throw new JsonSerializationException($"{subject} built-in target needs 'declaring', a closed type binding or a static class name.");
            }

            if (record["name"] is not JValue { Type: JTokenType.String } nameToken || nameToken.Value<string>()!.Length == 0)
                throw new JsonSerializationException($"{subject} built-in target needs a non-empty 'name'.");
            if (record["arity"] is not JValue { Type: JTokenType.Integer } arityToken)
                throw new JsonSerializationException($"{subject} built-in target needs an integer 'arity'.");
            long arity = arityToken.Value<long>();
            if (arity < 0)
                throw new JsonSerializationException($"{subject} built-in target has arity {arity}; arity is not negative.");
            if (arity > int.MaxValue)
                throw new JsonSerializationException($"{subject} built-in target has arity {arity}, above {int.MaxValue}.");

            if (record["parameterTypes"] is not JArray parameterTokens)
                throw new JsonSerializationException($"{subject} built-in target needs a 'parameterTypes' array.");
            var parameterTypes = new TypeBinding[parameterTokens.Count];
            for (int index = 0; index < parameterTypes.Length; index++)
            {
                string at = $"{subject}.parameterTypes[{index}]";
                if (parameterTokens[index] is not JObject parameter)
                    throw new JsonSerializationException($"{at} must be a type binding object.");
                TypeBinding read = TypeBindings.ReadParameterType(parameter, at, (int)arity);
                string? violation = TypeBindings.ParameterTypeViolation(read);
                if (violation is not null)
                    throw new JsonSerializationException($"{at} {violation}.");
                parameterTypes[index] = read;
            }

            TypeBinding? explicitInterface = null;
            if (record.Property("explicitInterface") is not null)
            {
                if (declaringClass is not null)
                    throw new JsonSerializationException($"{subject} built-in target declared by static class '{declaringClass}' has 'explicitInterface'; a static class implements no interface.");
                if (record["explicitInterface"] is not JObject interfaceToken)
                    throw new JsonSerializationException($"{subject}.explicitInterface must be a type binding object.");
                explicitInterface = ReadIdentity(interfaceToken, $"{subject}.explicitInterface");
                if (explicitInterface.Head!.Type != MemberKind.Interface)
                    throw new JsonSerializationException($"{subject}.explicitInterface names a {explicitInterface.Head.Type}; it must be a closed interface.");
            }

            TypeBinding[]? typeArguments = null;
            if (record.Property("typeArguments") is not null)
            {
                if (record["typeArguments"] is not JArray argumentTokens)
                    throw new JsonSerializationException($"{subject}.typeArguments must be an array.");
                if (argumentTokens.Count != arity)
                    throw new JsonSerializationException($"{subject} built-in target has {argumentTokens.Count} type argument(s) for arity {arity}.");
                typeArguments = new TypeBinding[argumentTokens.Count];
                for (int index = 0; index < typeArguments.Length; index++)
                {
                    string at = $"{subject}.typeArguments[{index}]";
                    if (argumentTokens[index] is not JObject argument)
                        throw new JsonSerializationException($"{at} must be a type binding object.");
                    typeArguments[index] = ReadIdentity(argument, at);
                }
            }
            if (arity == 0 && typeArguments is not null)
                throw new JsonSerializationException($"{subject} built-in target has arity 0 and 'typeArguments'; a non-generic method takes none.");
            if (arity > 0 && typeArguments is null)
                throw new JsonSerializationException($"{subject} built-in target has arity {arity} and no 'typeArguments'; a bound generic method has closed arguments.");

            TypeBinding? delegateType = null;
            if (record.Property("delegateType") is not null)
            {
                if (listener)
                    throw new JsonSerializationException($"{subject} carries 'delegateType'; a listener always has its action's delegate type.");
                if (record["delegateType"] is not JObject delegateToken)
                    throw new JsonSerializationException($"{subject}.delegateType must be a type binding object.");
                delegateType = ReadIdentity(delegateToken, $"{subject}.delegateType");
                if (delegateType.Head!.Type != MemberKind.NSDelegate)
                    throw new JsonSerializationException($"{subject}.delegateType names a {delegateType.Head.Type}; it must be a closed NeoDelegate.");
            }

            return new NeoBuiltInTarget(
                declaringType,
                declaringClass,
                nameToken.Value<string>()!,
                (int)arity,
                parameterTypes,
                explicitInterface,
                typeArguments,
                delegateType);
        }

        /// <summary>Reads a runtime-identity binding (<see cref="TypeBindingPosition.Identity"/>), refusing any other.</summary>
        internal static TypeBinding ReadIdentity(JToken? token, string subject)
        {
            if (token is not JObject obj)
                throw new JsonSerializationException($"{subject} must be a type binding object.");
            TypeBinding binding = TypeBindings.Read(obj, subject);
            string? violation = TypeBindings.PositionViolation(binding, TypeBindingPosition.Identity);
            if (violation is not null)
                throw new JsonSerializationException($"{subject} {violation}.");
            return binding;
        }

        /// <summary>Writes the built-in arm's fields into the open target object.</summary>
        internal void WriteFields(JsonWriter writer)
        {
            writer.WritePropertyName("kind");
            writer.WriteValue((int)NeoMemberTargetKind.BuiltIn);
            writer.WritePropertyName("declaring");
            if (DeclaringType is not null)
                TypeBindings.Write(writer, DeclaringType);
            else
                writer.WriteValue(DeclaringClass);
            writer.WritePropertyName("name");
            writer.WriteValue(Name);
            writer.WritePropertyName("arity");
            writer.WriteValue(Arity);
            writer.WritePropertyName("parameterTypes");
            WriteArray(writer, ParameterTypes);
            if (ExplicitInterface is not null)
            {
                writer.WritePropertyName("explicitInterface");
                TypeBindings.Write(writer, ExplicitInterface);
            }
            if (TypeArguments is not null)
            {
                writer.WritePropertyName("typeArguments");
                WriteArray(writer, TypeArguments);
            }
            if (DelegateType is not null)
            {
                writer.WritePropertyName("delegateType");
                TypeBindings.Write(writer, DelegateType);
            }
        }

        private static void WriteArray(JsonWriter writer, TypeBinding[] bindings)
        {
            writer.WriteStartArray();
            foreach (TypeBinding binding in bindings)
                TypeBindings.Write(writer, binding);
            writer.WriteEndArray();
        }

        public bool Equals(NeoBuiltInTarget? other) =>
            other is not null
            && Equals(DeclaringType, other.DeclaringType)
            && DeclaringClass == other.DeclaringClass
            && Name == other.Name
            && Arity == other.Arity
            && ArraysEqual(ParameterTypes, other.ParameterTypes)
            && Equals(ExplicitInterface, other.ExplicitInterface)
            && ArraysEqual(TypeArguments, other.TypeArguments)
            && Equals(DelegateType, other.DelegateType);

        public override bool Equals(object? obj) => Equals(obj as NeoBuiltInTarget);

        public override int GetHashCode()
        {
            int hash = HashCode.Combine(DeclaringType, DeclaringClass, Name, Arity, ExplicitInterface, DelegateType);
            foreach (TypeBinding parameter in ParameterTypes)
                hash = HashCode.Combine(hash, parameter);
            if (TypeArguments is not null)
            {
                foreach (TypeBinding argument in TypeArguments)
                    hash = HashCode.Combine(hash, argument);
            }
            return hash;
        }

        private static bool ArraysEqual(TypeBinding[]? left, TypeBinding[]? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
            {
                if (!left[index].Equals(right[index]))
                    return false;
            }
            return true;
        }
    }
}
