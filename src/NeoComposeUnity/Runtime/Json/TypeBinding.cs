// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>The arm of a <see cref="TypeBinding"/>. Numeric on the wire.</summary>
    public enum TypeBindingKind
    {
        /// <summary>A closed type: a head and, for a generic head, a binding per parameter.</summary>
        Closed = 0,
        /// <summary>A type parameter, <c>typeof(T)</c>.</summary>
        Open = 1,
        /// <summary>A generic definition, <c>typeof(List&lt;&gt;)</c>.</summary>
        Definition = 2,
        /// <summary>
        /// A built-in generic method's own type parameter by position, only
        /// inside a built-in target's parameter types.
        /// </summary>
        MethodParam = 3,
    }

    /// <summary>
    /// The stored position a binding sits in, which decides the arms it may
    /// take: a generic stamp, a runtime-identity position (a box's value
    /// type, a comparer's type, a construction, a closure or target key), or
    /// a <c>Type</c> payload.
    /// </summary>
    internal enum TypeBindingPosition
    {
        Stamp,
        Identity,
        TypePayload,
        /// <summary>
        /// A type argument a declaration spells (<c>TTypeArgument</c>): a
        /// closed type whose arms may forward a parameter, or a forwarded
        /// parameter.
        /// </summary>
        Argument,
    }

    /// <summary>
    /// P127 <c>TNSTypeHead</c>: a type info without its argument fields. A
    /// generic head's arguments are the <see cref="TypeBinding.Bindings"/>
    /// beside it. <see cref="Required"/> is the whole type's nullability, so
    /// <c>int?</c> is an Int head with <c>required: false</c>. A tuple
    /// head's <see cref="Names"/> are outside type identity, so equality
    /// ignores them.
    /// </summary>
    public sealed class TypeHead : IEquatable<TypeHead>
    {
        public TypeHead(
            MemberKind type,
            bool required,
            string? classId = null,
            string? interfaceId = null,
            string? enumId = null,
            NeoObjectTypeKind? objectType = null,
            string? opaqueName = null,
            IReadOnlyList<string>? names = null)
        {
            Type = type;
            Required = required;
            ClassId = classId;
            InterfaceId = interfaceId;
            EnumId = enumId;
            ObjectType = objectType;
            OpaqueName = opaqueName;
            Names = names;
        }

        /// <summary><see cref="MemberKind.Void"/> for a delegate's void result; <see cref="MemberKind.Unknown"/> for an opaque head.</summary>
        public MemberKind Type
        {
            get;
        }

        public bool Required
        {
            get;
        }

        public string? ClassId
        {
            get;
        }

        public string? InterfaceId
        {
            get;
        }

        public string? EnumId
        {
            get;
        }

        public NeoObjectTypeKind? ObjectType
        {
            get;
        }

        /// <summary>An opaque head's .NET full name (§2); set iff the head is opaque.</summary>
        public string? OpaqueName
        {
            get;
        }

        public bool IsOpaque => OpaqueName is not null;

        /// <summary>
        /// A tuple head's element labels in order, <c>""</c> for an
        /// unlabeled element (P127 §12.1); null when no element has one.
        /// Compile-time names, as C#'s <c>TupleElementNames</c> are, so only
        /// declarations and stamps keep them.
        /// </summary>
        public IReadOnlyList<string>? Names
        {
            get;
        }

        /// <summary>This head with <paramref name="required"/>.</summary>
        public TypeHead WithRequired(bool required) =>
            required == Required ? this : new TypeHead(Type, required, ClassId, InterfaceId, EnumId, ObjectType, OpaqueName, Names);

        /// <summary>This head without tuple labels, as a runtime type has none.</summary>
        public TypeHead WithoutNames() =>
            Names is null ? this : new TypeHead(Type, Required, ClassId, InterfaceId, EnumId, ObjectType, OpaqueName);

        public bool Equals(TypeHead? other) =>
            other is not null
            && Type == other.Type
            && Required == other.Required
            && ClassId == other.ClassId
            && InterfaceId == other.InterfaceId
            && EnumId == other.EnumId
            && ObjectType == other.ObjectType
            && OpaqueName == other.OpaqueName;

        public override bool Equals(object? obj) => Equals(obj as TypeHead);

        public override int GetHashCode() =>
            HashCode.Combine(Type, Required, ClassId, InterfaceId, EnumId, ObjectType, OpaqueName);
    }

    /// <summary>
    /// P127 <c>TNSTypeDefinition</c>: a generic type without its arguments,
    /// the owner of an <see cref="TypeBindingKind.Open"/> parameter or a
    /// <see cref="TypeBindingKind.Definition"/>.
    /// </summary>
    public sealed class TypeDefinition : IEquatable<TypeDefinition>
    {
        public TypeDefinition(
            MemberKind type,
            string? classId = null,
            string? interfaceId = null,
            int? arity = null,
            bool nullable = false)
        {
            Type = type;
            ClassId = classId;
            InterfaceId = interfaceId;
            Arity = arity;
            Nullable = nullable;
        }

        /// <summary>Class, Interface, List, Set, Dictionary, Reference, Tuple, NSDelegate or NSAction; <see cref="MemberKind.Unknown"/> for <c>Nullable&lt;&gt;</c>.</summary>
        public MemberKind Type
        {
            get;
        }

        public string? ClassId
        {
            get;
        }

        public string? InterfaceId
        {
            get;
        }

        /// <summary>A Tuple, NSDelegate or NSAction definition's parameter count.</summary>
        public int? Arity
        {
            get;
        }

        /// <summary><c>Nullable&lt;&gt;</c>.</summary>
        public bool Nullable
        {
            get;
        }

        /// <summary>The parameter count of a built-in definition; null for a class or interface.</summary>
        public int? BuiltInArity => Nullable
            ? 1
            : Type switch
            {
                MemberKind.List or MemberKind.Set or MemberKind.Reference => 1,
                MemberKind.Dictionary => 2,
                MemberKind.Tuple or MemberKind.NSDelegate or MemberKind.NSAction => Arity,
                _ => null,
            };

        public bool Equals(TypeDefinition? other) =>
            other is not null
            && Type == other.Type
            && ClassId == other.ClassId
            && InterfaceId == other.InterfaceId
            && Arity == other.Arity
            && Nullable == other.Nullable;

        public override bool Equals(object? obj) => Equals(obj as TypeDefinition);

        public override int GetHashCode() => HashCode.Combine(Type, ClassId, InterfaceId, Arity, Nullable);
    }

    /// <summary>
    /// P127 <c>TTypeBinding</c>: a runtime type as structured data, in every
    /// stored type position. A <see cref="TypeBindingKind.Closed"/> binding
    /// is a <see cref="Head"/> with one <see cref="Bindings"/> entry per
    /// parameter of a generic head: a declared class's or interface's by
    /// param id, a built-in's by decimal position in C# arity order (List,
    /// Set and Reference <c>"0"</c>; Dictionary <c>"0"</c> key and
    /// <c>"1"</c> value; tuples <c>"0"</c>..; NeoDelegate <c>"0"</c> the
    /// result, then its parameters; NeoAction its parameters from
    /// <c>"0"</c>). Immutable, with structural equality.
    /// </summary>
    [JsonConverter(typeof(TypeBindingConverter))]
    public sealed class TypeBinding : IEquatable<TypeBinding>
    {
        private int hash;

        private TypeBinding(
            TypeBindingKind kind,
            TypeHead? head,
            IReadOnlyDictionary<string, TypeBinding>? bindings,
            TypeDefinition? definition,
            string? param,
            int? index = null)
        {
            Kind = kind;
            Head = head;
            Bindings = bindings;
            Definition = definition;
            Param = param;
            Index = index;
        }

        public static TypeBinding Closed(TypeHead head, IReadOnlyDictionary<string, TypeBinding>? bindings = null) =>
            new(TypeBindingKind.Closed, head, bindings is { Count: > 0 } ? bindings : null, null, null);

        public static TypeBinding Open(TypeDefinition owner, string param) =>
            new(TypeBindingKind.Open, null, null, owner, param);

        public static TypeBinding OfDefinition(TypeDefinition definition) =>
            new(TypeBindingKind.Definition, null, null, definition, null);

        public static TypeBinding MethodParam(int index) =>
            new(TypeBindingKind.MethodParam, null, null, null, null, index);

        public TypeBindingKind Kind
        {
            get;
        }

        /// <summary>Whether this binding forwards a type parameter (<see cref="Param"/>).</summary>
        public bool IsOpen => Kind == TypeBindingKind.Open;

        /// <summary>A closed binding's head.</summary>
        public TypeHead? Head
        {
            get;
        }

        /// <summary>A closed generic head's argument per parameter key; null for a non-generic head.</summary>
        public IReadOnlyDictionary<string, TypeBinding>? Bindings
        {
            get;
        }

        /// <summary>An open binding's owner, or a definition binding's definition.</summary>
        public TypeDefinition? Definition
        {
            get;
        }

        /// <summary>An open binding's parameter: a declared owner's param id, or a built-in owner's position.</summary>
        public string? Param
        {
            get;
        }

        /// <summary>A method param binding's position among its method's type parameters.</summary>
        public int? Index
        {
            get;
        }

        /// <summary>This closed binding with its head's nullability set to <paramref name="required"/>.</summary>
        public TypeBinding WithRequired(bool required) =>
            Kind != TypeBindingKind.Closed || Head!.Required == required
                ? this
                : new TypeBinding(TypeBindingKind.Closed, Head.WithRequired(required), Bindings, null, null);

        /// <summary>The closed binding at decimal position <paramref name="index"/>.</summary>
        public TypeBinding At(int index) => Bindings![index.ToString(CultureInfo.InvariantCulture)];

        public bool Equals(TypeBinding? other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other is null
                || Kind != other.Kind
                || GetHashCode() != other.GetHashCode()
                || !Equals(Head, other.Head)
                || !Equals(Definition, other.Definition)
                || Param != other.Param
                || Index != other.Index)
            {
                return false;
            }
            return BindingsEqual(Bindings, other.Bindings);
        }

        /// <summary>Whether two binding maps hold equal bindings under the same keys.</summary>
        public static bool BindingsEqual(
            IReadOnlyDictionary<string, TypeBinding>? left,
            IReadOnlyDictionary<string, TypeBinding>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if ((left?.Count ?? 0) != (right?.Count ?? 0))
                return false;
            if (left is null || right is null)
                return true;
            foreach (KeyValuePair<string, TypeBinding> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out TypeBinding? other) || !pair.Value.Equals(other))
                    return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as TypeBinding);

        public override int GetHashCode()
        {
            if (hash != 0)
                return hash;
            int combined = HashCode.Combine(Kind, Head, Definition, Param, Index);
            if (Bindings is not null)
            {
                // Order-independent, as Bindings is a map.
                int entries = 0;
                foreach (KeyValuePair<string, TypeBinding> pair in Bindings)
                    entries ^= HashCode.Combine(pair.Key, pair.Value.GetHashCode());
                combined = HashCode.Combine(combined, entries);
            }
            hash = combined == 0 ? 1 : combined;
            return hash;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="TypeBinding"/>, refusing any binding the
    /// shape rules refuse in every position (<see cref="TypeBindings.Read"/>).
    /// A position's own rules are <see cref="TypeBindings.PositionViolation"/>.
    /// </summary>
    public sealed class TypeBindingConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(TypeBinding);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            string path = reader.Path;
            JObject obj = NeoJsonObjectReader.Read(reader);
            return TypeBindings.Read(obj, string.IsNullOrEmpty(path) ? "type binding" : path);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }
            TypeBindings.Write(writer, (TypeBinding)value);
        }
    }

    /// <summary>
    /// A row's generic stamp: param id to the closed type it binds. Refuses
    /// any binding a stamp can't hold (<see cref="TypeBindingPosition.Stamp"/>).
    /// </summary>
    public sealed class GenericBindingsStampConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<string, TypeBinding>);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            string path = reader.Path;
            JObject obj = NeoJsonObjectReader.Read(reader);
            var stamp = new Dictionary<string, TypeBinding>(obj.Count, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
            {
                string subject = $"{path}.{property.Name}";
                if (property.Name.Length == 0)
                    throw new JsonSerializationException($"{path} has an empty param id.");
                if (property.Value is not JObject binding)
                    throw new JsonSerializationException($"{subject} must be a type binding object.");
                TypeBinding read = TypeBindings.Read(binding, subject);
                string? violation = TypeBindings.PositionViolation(read, TypeBindingPosition.Stamp);
                if (violation is not null)
                    throw new JsonSerializationException($"{subject} {violation}.");
                stamp[property.Name] = read;
            }
            return stamp;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            foreach (KeyValuePair<string, TypeBinding> pair in (Dictionary<string, TypeBinding>)value)
            {
                writer.WritePropertyName(pair.Key);
                TypeBindings.Write(writer, pair.Value);
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// A declaration's type arguments (P127 §12.1): param id to a
    /// <see cref="TypeBindingPosition.Argument"/> binding, closed or a
    /// forwarded parameter. Refuses an empty map, which a declaration omits.
    /// </summary>
    public sealed class TypeArgumentsConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<string, TypeBinding>);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            string path = reader.Path;
            JObject obj = NeoJsonObjectReader.Read(reader);
            return TypeBindings.ReadArguments(obj, string.IsNullOrEmpty(path) ? "type arguments" : path);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            foreach (KeyValuePair<string, TypeBinding> pair in (Dictionary<string, TypeBinding>)value)
            {
                writer.WritePropertyName(pair.Key);
                TypeBindings.Write(writer, pair.Value);
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The shape rules of <see cref="TypeBinding"/> and its wire form. Every
    /// position refuses: a head with argument fields (they go in
    /// <c>bindings</c>), an Unknown, Lookup or Generic head, a void head
    /// outside a NeoDelegate's result, a generic head that doesn't bind
    /// each of its positions, a non-generic one that binds any, a nested
    /// definition and a definition of arity 0.
    /// </summary>
    internal static class TypeBindings
    {
        internal const int NeoDelegateMaxArity = 17;
        internal const int NeoActionMaxArity = 16;
        internal const int ValueTupleMaxArity = 8;

        /// <summary>§2's opaque types by .NET full name.</summary>
        internal static readonly string[] OpaqueTypes =
        {
            "System.Reflection.MemberInfo",
            "System.Delegate",
            "System.MulticastDelegate",
            "UnityEngine.Object",
            "NeoCompose.Runtime.NeoActionBase",
        };

        /// <summary>Where a binding sits under its parent, for the rules only that place has.</summary>
        private enum Place
        {
            Root,
            Element,
            DelegateResult,
            DictionaryKey,
        }

        internal static TypeBinding Read(JObject obj, string subject) => ReadBinding(obj, subject, Place.Root, null);

        /// <summary>
        /// Reads a declaration's type arguments; see <see cref="TypeArgumentsConverter"/>.
        /// An empty object, which project exports write for a declaration
        /// with no type arguments, reads as none.
        /// </summary>
        internal static Dictionary<string, TypeBinding>? ReadArguments(JObject obj, string subject)
        {
            if (obj.Count == 0)
                return null;
            var arguments = new Dictionary<string, TypeBinding>(obj.Count, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
            {
                string at = $"{subject}.{property.Name}";
                if (property.Name.Length == 0)
                    throw new JsonSerializationException($"{subject} has an empty param id.");
                if (property.Value is not JObject binding)
                    throw new JsonSerializationException($"{at} must be a type binding object.");
                TypeBinding read = Read(binding, at);
                string? violation = PositionViolation(read, TypeBindingPosition.Argument);
                if (violation is not null)
                    throw new JsonSerializationException($"{at} {violation}.");
                arguments[property.Name] = read;
            }
            return arguments;
        }

        /// <summary>
        /// Reads a built-in target's parameter type, the one place a
        /// <see cref="TypeBindingKind.MethodParam"/> may appear, at any depth,
        /// with an index below the target's <paramref name="arity"/>.
        /// </summary>
        internal static TypeBinding ReadParameterType(JObject obj, string subject, int arity) =>
            ReadBinding(obj, subject, Place.Root, arity);

        /// <remarks><paramref name="methodArity"/> is the enclosing built-in target's generic arity, or null outside its parameter types.</remarks>
        private static TypeBinding ReadBinding(JObject obj, string subject, Place place, int? methodArity)
        {
            if (obj["kind"] is not JValue { Type: JTokenType.Integer } kindToken)
                throw new JsonSerializationException($"{subject} needs an integer 'kind'.");
            long kind = kindToken.Value<long>();
            switch (kind)
            {
                case (long)TypeBindingKind.Closed:
                    return ReadClosed(obj, subject, place, methodArity);
                case (long)TypeBindingKind.Open:
                    return ReadOpen(obj, subject);
                case (long)TypeBindingKind.Definition:
                    if (place != Place.Root)
                        throw new JsonSerializationException($"{subject} nests a definition; a definition only stands alone.");
                    return ReadDefinitionBinding(obj, subject);
                case (long)TypeBindingKind.MethodParam:
                    return ReadMethodParam(obj, subject, place, methodArity);
                default:
                    throw new JsonSerializationException($"{subject} has unknown kind {kind}.");
            }
        }

        private static TypeBinding ReadMethodParam(JObject obj, string subject, Place place, int? methodArity)
        {
            if (methodArity is not int arity)
                throw new JsonSerializationException($"{subject} is a method type parameter; only a built-in target's parameterTypes hold one.");
            if (place == Place.DictionaryKey)
                throw new JsonSerializationException($"{subject} keys a Dictionary by a method type parameter; Dictionary keys are string or an enum.");
            RequireOnly(obj, subject, "kind", "index");
            if (obj["index"] is not JValue { Type: JTokenType.Integer } indexToken)
                throw new JsonSerializationException($"{subject} needs an integer 'index'.");
            long index = indexToken.Value<long>();
            if (index < 0)
                throw new JsonSerializationException($"{subject} has index {index}; a method type parameter's index is not negative.");
            if (index >= arity)
                throw new JsonSerializationException($"{subject} has index {index}; its method has {arity} type parameter(s).");
            return TypeBinding.MethodParam((int)index);
        }

        private static TypeBinding ReadClosed(JObject obj, string subject, Place place, int? methodArity)
        {
            RequireOnly(obj, subject, "kind", "type", "bindings");
            if (obj["type"] is not JObject headToken)
                throw new JsonSerializationException($"{subject} needs a 'type' head object.");
            TypeHead head = ReadHead(headToken, $"{subject}.type", place);
            Dictionary<string, TypeBinding>? bindings = null;
            JToken? bindingsToken = obj["bindings"];
            if (bindingsToken is not null && bindingsToken.Type != JTokenType.Null)
            {
                if (bindingsToken is not JObject bindingsObject)
                    throw new JsonSerializationException($"{subject}.bindings must be an object.");
                bindings = ReadBindings(bindingsObject, $"{subject}.bindings", head, methodArity);
            }
            string? violation = ArityViolation(head, bindings);
            if (violation is not null)
                throw new JsonSerializationException($"{subject} {violation}.");
            if (head.Names is { } names && names.Count != bindings!.Count)
                throw new JsonSerializationException($"{subject} labels {names.Count} of its {bindings.Count} elements; a tuple labels each element, \"\" for none.");
            if (place == Place.DictionaryKey && head.Type is not MemberKind.String and not MemberKind.Enum)
                throw new JsonSerializationException($"{subject} keys a Dictionary by {NeoTypeNames.Of(TypeBinding.Closed(head, bindings))}; Dictionary keys are string or an enum.");
            if (place == Place.DictionaryKey && !head.Required)
                throw new JsonSerializationException($"{subject} keys a Dictionary by a nullable type.");
            return TypeBinding.Closed(head, bindings);
        }

        private static Dictionary<string, TypeBinding> ReadBindings(JObject obj, string subject, TypeHead head, int? methodArity)
        {
            var bindings = new Dictionary<string, TypeBinding>(obj.Count, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
            {
                string key = property.Name;
                string at = $"{subject}.{key}";
                if (key.Length == 0)
                    throw new JsonSerializationException($"{subject} has an empty key.");
                if (property.Value is not JObject value)
                    throw new JsonSerializationException($"{at} must be a type binding object.");
                Place place = head.Type switch
                {
                    MemberKind.NSDelegate when key == "0" => Place.DelegateResult,
                    MemberKind.Dictionary when key == "0" => Place.DictionaryKey,
                    _ => Place.Element,
                };
                bindings[key] = ReadBinding(value, at, place, methodArity);
            }
            return bindings;
        }

        /// <summary>Why <paramref name="bindings"/> doesn't bind exactly <paramref name="head"/>'s positions, or null.</summary>
        private static string? ArityViolation(TypeHead head, IReadOnlyDictionary<string, TypeBinding>? bindings)
        {
            int count = bindings?.Count ?? 0;
            if (bindings is { Count: 0 })
                return "has empty bindings; a non-generic head omits them";
            switch (head.Type)
            {
                case MemberKind.Class:
                case MemberKind.Interface:
                    // A declared head binds its params by id; the project knows which.
                    return null;
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Reference:
                    return PositionsViolation(head, bindings, 1, 1);
                case MemberKind.Dictionary:
                    return PositionsViolation(head, bindings, 2, 2);
                case MemberKind.Tuple:
                    return PositionsViolation(head, bindings, 1, int.MaxValue);
                case MemberKind.NSDelegate:
                    return PositionsViolation(head, bindings, 1, NeoDelegateMaxArity);
                case MemberKind.NSAction:
                    // A zero-param NeoAction is the non-generic head.
                    return count == 0 ? null : PositionsViolation(head, bindings, 1, NeoActionMaxArity);
                default:
                    return count == 0 ? null : $"binds type arguments to {NeoTypeNames.Head(head)}, which is not generic";
            }
        }

        private static string? PositionsViolation(TypeHead head, IReadOnlyDictionary<string, TypeBinding>? bindings, int min, int max)
        {
            int count = bindings?.Count ?? 0;
            if (count < min)
                return $"binds {count} of {NeoTypeNames.Head(head)}'s positions; it needs at least {min}";
            if (count > max)
                return $"binds {count} of {NeoTypeNames.Head(head)}'s positions; it takes at most {max}";
            for (int index = 0; index < count; index++)
            {
                string key = index.ToString(CultureInfo.InvariantCulture);
                if (!bindings!.ContainsKey(key))
                    return $"doesn't bind {NeoTypeNames.Head(head)}'s position \"{key}\"; built-in positions are \"0\" to \"{count - 1}\"";
            }
            return null;
        }

        private static TypeHead ReadHead(JObject obj, string subject, Place place)
        {
            JToken? typeToken = obj["type"];
            if (typeToken is JValue { Type: JTokenType.String } named)
                return ReadNamedHead(obj, subject, named.Value<string>()!, place);
            if (typeToken is not JValue { Type: JTokenType.Integer } number)
                throw new JsonSerializationException($"{subject} needs a 'type'.");
            var type = (MemberKind)number.Value<int>();
            bool required = ReadRequired(obj, subject);
            switch (type)
            {
                case MemberKind.Class:
                    RequireOnly(obj, subject, "type", "required", "classId");
                    return new TypeHead(type, required, classId: ReadId(obj, subject, "classId"));
                case MemberKind.Interface:
                    RequireOnly(obj, subject, "type", "required", "interfaceId");
                    return new TypeHead(type, required, interfaceId: ReadId(obj, subject, "interfaceId"));
                case MemberKind.Enum:
                    RequireOnly(obj, subject, "type", "required", "enumId");
                    return new TypeHead(type, required, enumId: ReadId(obj, subject, "enumId"));
                case MemberKind.Object:
                    RequireOnly(obj, subject, "type", "required", "objectType");
                    StrictRecordShapeEnums.ValidateOptional<NeoObjectTypeKind>(obj, "objectType", subject);
                    NeoObjectTypeKind? objectType = obj["objectType"] is JValue { Type: JTokenType.Integer } objectToken
                        ? (NeoObjectTypeKind)objectToken.Value<int>()
                        : null;
                    return new TypeHead(type, required, objectType: objectType);
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.Decimal:
                case MemberKind.String:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                case MemberKind.DialogueLookup:
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                case MemberKind.Reference:
                case MemberKind.NSDelegate:
                case MemberKind.NSAction:
                    RequireOnly(obj, subject, "type", "required");
                    return new TypeHead(type, required);
                case MemberKind.Tuple:
                    RequireOnly(obj, subject, "type", "required", "names");
                    return new TypeHead(type, required, names: ReadNames(obj, subject));
                case MemberKind.Lookup:
                    throw new JsonSerializationException($"{subject} is a Lookup head; a Lookup binds as the Reference or Set it holds.");
                case MemberKind.Generic:
                    throw new JsonSerializationException($"{subject} is a generic parameter head; a binding names a closed type or an Open parameter.");
                default:
                    throw new JsonSerializationException($"{subject} has type {(int)type}, which names no bindable type.");
            }
        }

        /// <summary>A tuple head's labels: omitted, or one string per element with at least one label.</summary>
        private static string[]? ReadNames(JObject obj, string subject)
        {
            JToken? token = obj["names"];
            if (token is null)
                return null;
            if (token is not JArray array)
                throw new JsonSerializationException($"{subject} 'names' must be an array of labels.");
            var names = new string[array.Count];
            bool labeled = false;
            for (int index = 0; index < names.Length; index++)
            {
                if (array[index] is not JValue { Type: JTokenType.String } label)
                    throw new JsonSerializationException($"{subject} 'names' must hold strings.");
                names[index] = label.Value<string>()!;
                labeled |= names[index].Length != 0;
            }
            if (!labeled)
                throw new JsonSerializationException($"{subject} spells 'names' where no element has a label; it omits them.");
            return names;
        }

        private static TypeHead ReadNamedHead(JObject obj, string subject, string name, Place place)
        {
            switch (name)
            {
                case "Void":
                    if (place != Place.DelegateResult)
                        throw new JsonSerializationException($"{subject} is void outside a NeoDelegate's result.");
                    RequireOnly(obj, subject, "type", "required");
                    if (!ReadRequired(obj, subject))
                        throw new JsonSerializationException($"{subject} spells void with required false; void is required.");
                    return new TypeHead(MemberKind.Void, true);
                case "Opaque":
                    {
                        RequireOnly(obj, subject, "type", "required", "name");
                        if (!ReadRequired(obj, subject))
                            throw new JsonSerializationException($"{subject} is an opaque head with required false; opaque heads are required.");
                        string opaqueName = ReadId(obj, subject, "name");
                        if (Array.IndexOf(OpaqueTypes, opaqueName) < 0)
                            throw new JsonSerializationException($"{subject} names unknown opaque type '{opaqueName}'.");
                        return new TypeHead(MemberKind.Unknown, true, opaqueName: opaqueName);
                    }
                case "Unknown":
                    throw new JsonSerializationException($"{subject} is an Unknown head, which names no runtime type.");
                default:
                    throw new JsonSerializationException($"{subject} has type '{name}', which names no bindable type.");
            }
        }

        private static TypeBinding ReadOpen(JObject obj, string subject)
        {
            RequireOnly(obj, subject, "kind", "owner", "param");
            if (obj["owner"] is not JObject ownerToken)
                throw new JsonSerializationException($"{subject} needs an 'owner' definition object.");
            TypeDefinition owner = ReadDefinition(ownerToken, $"{subject}.owner");
            string param = ReadId(obj, subject, "param");
            if (owner.BuiltInArity is int arity)
            {
                if (!IsCanonicalIndex(param, out int index))
                    throw new JsonSerializationException($"{subject} names param '{param}' of a built-in, whose params are decimal positions.");
                if (index >= arity)
                    throw new JsonSerializationException($"{subject} names param {index} of a definition with {arity}.");
            }
            return TypeBinding.Open(owner, param);
        }

        private static TypeBinding ReadDefinitionBinding(JObject obj, string subject)
        {
            RequireOnly(obj, subject, "kind", "type");
            if (obj["type"] is not JObject definitionToken)
                throw new JsonSerializationException($"{subject} needs a 'type' definition object.");
            return TypeBinding.OfDefinition(ReadDefinition(definitionToken, $"{subject}.type"));
        }

        private static TypeDefinition ReadDefinition(JObject obj, string subject)
        {
            JToken? typeToken = obj["type"];
            if (typeToken is JValue { Type: JTokenType.String } named)
            {
                if (named.Value<string>() != "Nullable")
                    throw new JsonSerializationException($"{subject} has type '{named}', which defines no generic type.");
                RequireOnly(obj, subject, "type");
                return new TypeDefinition(MemberKind.Unknown, nullable: true);
            }
            if (typeToken is not JValue { Type: JTokenType.Integer } number)
                throw new JsonSerializationException($"{subject} needs a 'type'.");
            var type = (MemberKind)number.Value<int>();
            switch (type)
            {
                case MemberKind.Class:
                    RequireOnly(obj, subject, "type", "classId");
                    return new TypeDefinition(type, classId: ReadId(obj, subject, "classId"));
                case MemberKind.Interface:
                    RequireOnly(obj, subject, "type", "interfaceId");
                    return new TypeDefinition(type, interfaceId: ReadId(obj, subject, "interfaceId"));
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                case MemberKind.Reference:
                    RequireOnly(obj, subject, "type");
                    return new TypeDefinition(type);
                case MemberKind.Tuple:
                case MemberKind.NSDelegate:
                case MemberKind.NSAction:
                    {
                        RequireOnly(obj, subject, "type", "arity");
                        if (obj["arity"] is not JValue { Type: JTokenType.Integer } arityToken)
                            throw new JsonSerializationException($"{subject} needs an integer 'arity'.");
                        long arity = arityToken.Value<long>();
                        int max = type switch
                        {
                            MemberKind.Tuple => ValueTupleMaxArity,
                            MemberKind.NSDelegate => NeoDelegateMaxArity,
                            _ => NeoActionMaxArity,
                        };
                        if (arity < 1)
                            throw new JsonSerializationException($"{subject} has arity {arity}; a definition has at least one parameter.");
                        if (arity > max)
                            throw new JsonSerializationException($"{subject} has arity {arity}, above {max}.");
                        return new TypeDefinition(type, arity: (int)arity);
                    }
                default:
                    throw new JsonSerializationException($"{subject} has type {(int)type}, which defines no generic type.");
            }
        }

        private static bool ReadRequired(JObject obj, string subject)
        {
            if (obj["required"] is not JValue { Type: JTokenType.Boolean } required)
                throw new JsonSerializationException($"{subject} needs a boolean 'required'.");
            return required.Value<bool>();
        }

        private static string ReadId(JObject obj, string subject, string field)
        {
            if (obj[field] is not JValue { Type: JTokenType.String } value || value.Value<string>()!.Length == 0)
                throw new JsonSerializationException($"{subject} needs a non-empty '{field}'.");
            return value.Value<string>()!;
        }

        private static void RequireOnly(JObject obj, string subject, params string[] allowed)
        {
            foreach (JProperty property in obj.Properties())
            {
                if (Array.IndexOf(allowed, property.Name) < 0)
                    throw new JsonSerializationException($"{subject} has '{property.Name}', which it doesn't take.");
            }
        }

        private static bool IsCanonicalIndex(string text, out int index)
        {
            index = 0;
            if (text.Length == 0 || (text.Length > 1 && text[0] == '0'))
                return false;
            foreach (char character in text)
            {
                if (character < '0' || character > '9')
                    return false;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        /// <summary>
        /// The first rule <paramref name="binding"/> breaks in
        /// <paramref name="position"/>, or null. A stamp and a
        /// runtime-identity position hold only closed bindings, with no
        /// opaque head; a runtime-identity position also needs every reference-type head
        /// required, as a runtime type has no nullable reference. A
        /// <c>Type</c> payload takes any binding.
        /// </summary>
        internal static string? PositionViolation(TypeBinding binding, TypeBindingPosition position)
        {
            if ((position is TypeBindingPosition.Identity or TypeBindingPosition.TypePayload) && NamesIn(binding))
                return NamesViolation;
            if (position == TypeBindingPosition.TypePayload)
                return MethodParamIn(binding) ? MethodParamViolation : null;
            if (position == TypeBindingPosition.Argument && binding.Kind == TypeBindingKind.Open)
                return null;
            switch (binding.Kind)
            {
                case TypeBindingKind.Open:
                    return position == TypeBindingPosition.Stamp
                        ? $"forwards param '{binding.Param}', which a stamp can't hold yet"
                        : $"is open param '{binding.Param}'; only a Type payload holds an open type";
                case TypeBindingKind.Definition:
                    return "is a generic definition; only a Type payload holds one";
                case TypeBindingKind.MethodParam:
                    return MethodParamViolation;
            }
            TypeHead head = binding.Head!;
            if (head.IsOpaque)
                return $"names opaque type '{head.OpaqueName}'; only a Type payload names one";
            if (position == TypeBindingPosition.Identity && !head.Required && !IsValueType(head))
                return $"is {NeoTypeNames.Of(binding)}; a runtime type has no nullable reference";
            if (binding.Bindings is null)
                return null;
            foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
            {
                string? violation = PositionViolation(pair.Value, position);
                if (violation is not null)
                    return $"at \"{pair.Key}\" {violation}";
            }
            return null;
        }

        private const string MethodParamViolation = "is a method type parameter; only a built-in target's parameterTypes hold one";

        /// <summary>
        /// The first rule a built-in target's parameter type breaks, or null:
        /// a runtime-identity position whose method type parameters
        /// (<see cref="TypeBindingKind.MethodParam"/>) the reader already
        /// range-checked against the target's arity.
        /// </summary>
        internal static string? ParameterTypeViolation(TypeBinding binding)
        {
            if (binding.Kind == TypeBindingKind.MethodParam)
                return null;
            if (binding.Kind != TypeBindingKind.Closed || binding.Bindings is null)
                return PositionViolation(binding, TypeBindingPosition.Identity);
            TypeHead head = binding.Head!;
            if (!head.Required && !IsValueType(head))
                return $"is {NeoTypeNames.Of(binding)}; a runtime type has no nullable reference";
            foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
            {
                string? violation = ParameterTypeViolation(pair.Value);
                if (violation is not null)
                    return $"at \"{pair.Key}\" {violation}";
            }
            return null;
        }

        internal const string NamesViolation = "carries tuple labels; a runtime type erases them";

        /// <summary>Whether a tuple head at any depth carries labels.</summary>
        internal static bool NamesIn(TypeBinding binding)
        {
            if (binding.Head?.Names is not null)
                return true;
            if (binding.Bindings is null)
                return false;
            foreach (TypeBinding nested in binding.Bindings.Values)
            {
                if (NamesIn(nested))
                    return true;
            }
            return false;
        }

        /// <summary><paramref name="binding"/> without tuple labels at any depth; itself when it carries none.</summary>
        internal static TypeBinding WithoutNames(TypeBinding binding)
        {
            if (!NamesIn(binding))
                return binding;
            Dictionary<string, TypeBinding>? bindings = null;
            if (binding.Bindings is not null)
            {
                bindings = new Dictionary<string, TypeBinding>(binding.Bindings.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
                    bindings[pair.Key] = WithoutNames(pair.Value);
            }
            return TypeBinding.Closed(binding.Head!.WithoutNames(), bindings);
        }

        private static bool MethodParamIn(TypeBinding binding)
        {
            if (binding.Kind == TypeBindingKind.MethodParam)
                return true;
            if (binding.Bindings is null)
                return false;
            foreach (TypeBinding nested in binding.Bindings.Values)
            {
                if (MethodParamIn(nested))
                    return true;
            }
            return false;
        }

        /// <summary>Whether a head is a value type, whose <c>?</c> is a distinct runtime type.</summary>
        internal static bool IsValueType(TypeHead head) => head.Type switch
        {
            MemberKind.Bool
                or MemberKind.Int
                or MemberKind.Float
                or MemberKind.Decimal
                or MemberKind.Vector2
                or MemberKind.Vector2Int
                or MemberKind.Vector3
                or MemberKind.Vector3Int
                or MemberKind.Color
                or MemberKind.Enum
                or MemberKind.Tuple => true,
            _ => false,
        };

        internal static void Write(JsonWriter writer, TypeBinding binding)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            writer.WriteValue((int)binding.Kind);
            switch (binding.Kind)
            {
                case TypeBindingKind.Closed:
                    writer.WritePropertyName("type");
                    WriteHead(writer, binding.Head!);
                    if (binding.Bindings is not null)
                    {
                        writer.WritePropertyName("bindings");
                        writer.WriteStartObject();
                        foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
                        {
                            writer.WritePropertyName(pair.Key);
                            Write(writer, pair.Value);
                        }
                        writer.WriteEndObject();
                    }
                    break;
                case TypeBindingKind.Open:
                    writer.WritePropertyName("owner");
                    WriteDefinition(writer, binding.Definition!);
                    writer.WritePropertyName("param");
                    writer.WriteValue(binding.Param);
                    break;
                case TypeBindingKind.MethodParam:
                    writer.WritePropertyName("index");
                    writer.WriteValue(binding.Index!.Value);
                    break;
                default:
                    writer.WritePropertyName("type");
                    WriteDefinition(writer, binding.Definition!);
                    break;
            }
            writer.WriteEndObject();
        }

        private static void WriteHead(JsonWriter writer, TypeHead head)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("type");
            if (head.IsOpaque)
                writer.WriteValue("Opaque");
            else if (head.Type == MemberKind.Void)
                writer.WriteValue("Void");
            else
                writer.WriteValue((int)head.Type);
            writer.WritePropertyName("required");
            writer.WriteValue(head.Required);
            WriteOptional(writer, "classId", head.ClassId);
            WriteOptional(writer, "interfaceId", head.InterfaceId);
            WriteOptional(writer, "enumId", head.EnumId);
            if (head.ObjectType is { } objectType)
            {
                writer.WritePropertyName("objectType");
                writer.WriteValue((int)objectType);
            }
            WriteOptional(writer, "name", head.OpaqueName);
            if (head.Names is { } names)
            {
                writer.WritePropertyName("names");
                writer.WriteStartArray();
                foreach (string name in names)
                    writer.WriteValue(name);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        private static void WriteDefinition(JsonWriter writer, TypeDefinition definition)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("type");
            if (definition.Nullable)
                writer.WriteValue("Nullable");
            else
                writer.WriteValue((int)definition.Type);
            WriteOptional(writer, "classId", definition.ClassId);
            WriteOptional(writer, "interfaceId", definition.InterfaceId);
            if (definition.Arity is { } arity)
            {
                writer.WritePropertyName("arity");
                writer.WriteValue(arity);
            }
            writer.WriteEndObject();
        }

        private static void WriteOptional(JsonWriter writer, string name, string? value)
        {
            if (value is null)
                return;
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }
    }
}
