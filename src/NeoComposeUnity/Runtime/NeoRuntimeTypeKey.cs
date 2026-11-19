// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace NeoCompose.Runtime
{
    internal enum NeoRuntimeTypeKeyKind
    {
        Builtin,
        BuiltinConstruction,
        Void,
        Class,
        Interface,
        Enum,
        Tuple,
        ValueTuple,
        Nullable,
        Definition,
        Parameter,
        Opaque,
    }

    /// <summary>
    /// P127 §5.3. One parsed runtime type key: the canonical spelling of a
    /// runtime type, computed in memory from a stored type binding and never
    /// stored. Twin of the TypeScript
    /// <c>runtime-type-key.ts</c> in <c>@neocompose/neoscript-language</c>,
    /// with the same grammar, spelling and messages.
    ///
    /// <para><see cref="Name"/> holds a built-in's name, an opaque type's
    /// .NET name, or the owner name of a built-in definition or parameter;
    /// <see cref="Id"/> holds a <c>c:</c>, <c>i:</c> or <c>e:</c> id, or the
    /// id of a class or interface owner. <see cref="OwnerKind"/> is
    /// <see cref="NeoRuntimeTypeKeyKind.Builtin"/>, <c>Class</c> or
    /// <c>Interface</c> for a definition or parameter.</para>
    /// </summary>
    internal sealed class NeoRuntimeTypeKey
    {
        internal NeoRuntimeTypeKeyKind Kind;
        internal string? Name;
        internal string? Id;
        /// <summary>Type arguments, tuple elements, or the one ValueTuple or nullable operand.</summary>
        internal NeoRuntimeTypeKey[]? Arguments;
        internal NeoRuntimeTypeKeyKind OwnerKind;
        /// <summary>A definition's arity or a parameter's index.</summary>
        internal int Number;

        public override string ToString() => NeoRuntimeTypeKeys.Spell(this);
    }

    /// <summary>
    /// P127 §5.3's runtime type key grammar: <see cref="TryParse"/>,
    /// <see cref="Spell"/> (the inverse of the parser) and
    /// <see cref="Violation"/>, which applies the structural validation. The
    /// project-aware checks (each id names a type of its kind, with its
    /// arity) belong to the server, which has the version's types.
    /// </summary>
    internal static class NeoRuntimeTypeKeys
    {
        /// <summary>P127's built-in non-generic types (§1.3, §2, §11).</summary>
        internal static readonly string[] P127Builtins =
        {
            "object",
            "ValueType",
            "Enum",
            "Type",
            "CultureInfo",
            "NumberFormatInfo",
        };

        /// <summary>
        /// §2's opaque types by .NET full name, the only names <c>opaque:</c>
        /// accepts. <c>NeoActionBase</c> is <c>NeoAction</c>'s base, the SDK
        /// class generated C# derives from.
        /// </summary>
        internal static readonly string[] OpaqueTypes =
        {
            "System.Reflection.MemberInfo",
            "System.Delegate",
            "System.MulticastDelegate",
            "UnityEngine.Object",
            "NeoCompose.Runtime.NeoActionBase",
        };

        private const string NeoDelegate = "NeoDelegate";
        private const int NeoDelegateMaxArity = 17;
        private const string NeoAction = "NeoAction";
        private const int NeoActionMaxArity = 16;
        private const string VoidName = "void";
        private const string NullableDefinition = "Nullable";
        private const string ValueTupleName = "ValueTuple";
        private const int ValueTupleMaxArity = 8;

        private static readonly Dictionary<string, int> FixedArityGenerics = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["List"] = 1,
            ["Set"] = 1,
            ["Reference"] = 1,
            ["Dictionary"] = 2,
        };

        /// <summary>
        /// The primitives, the non-generic runtime names among
        /// <c>NEOSCRIPT_BUILTIN_ANNOTATION_TYPES</c>, and P127's built-ins.
        /// </summary>
        private static readonly HashSet<string> NonGenericBuiltins = new HashSet<string>(StringComparer.Ordinal)
        {
            "int",
            "float",
            "decimal",
            "bool",
            "string",
            "SpriteInfo",
            "AudioClipInfo",
            "NeoDialogueReference",
            "Vector2",
            "Vector2Int",
            "Vector3",
            "Vector3Int",
            "Color",
            "object",
            "ValueType",
            "Enum",
            "Type",
            "CultureInfo",
            "NumberFormatInfo",
        };

        /// <summary>Built-in value types: the only built-ins a <c>?</c> may follow.</summary>
        private static readonly HashSet<string> BuiltinValueTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "int",
            "float",
            "decimal",
            "bool",
            "Vector2",
            "Vector2Int",
            "Vector3",
            "Vector3Int",
            "Color",
        };

        /// <summary>Reads one runtime type key under §5.3's grammar.</summary>
        internal static bool TryParse(string text, out NeoRuntimeTypeKey key, out string error)
        {
            try
            {
                key = new Parser(text).Parse();
                error = "";
                return true;
            }
            catch (FormatException exception)
            {
                key = null!;
                error = exception.Message;
                return false;
            }
        }

        /// <summary>Whether <paramref name="name"/> is a built-in type with a runtime type key.</summary>
        internal static bool IsBuiltin(string name) =>
            NonGenericBuiltins.Contains(name)
            || FixedArityGenerics.ContainsKey(name)
            || name == NeoDelegate
            || name == NeoAction;

        /// <summary>
        /// The first §5.3 structural validation failure of
        /// <paramref name="text"/>, or null. <paramref name="closed"/> requires
        /// every position closed (keys computed from stored bindings);
        /// <paramref name="allowOpaque"/>
        /// admits <c>opaque:</c>, which only <c>Type</c> payloads may hold.
        /// </summary>
        internal static string? Violation(string text, bool closed = false, bool allowOpaque = false) =>
            Violation(text, out _, closed, allowOpaque);

        /// <inheritdoc cref="Violation(string, bool, bool)"/>
        /// <param name="key">The parsed key, or null when the text doesn't parse.</param>
        internal static string? Violation(string text, out NeoRuntimeTypeKey? key, bool closed = false, bool allowOpaque = false)
        {
            if (!TryParse(text, out NeoRuntimeTypeKey parsed, out string error))
            {
                key = null;
                return $"'{text}' is not a runtime type key: {error}.";
            }
            key = parsed;
            return ParsedViolation(text, parsed, closed, allowOpaque);
        }

        private static string? ParsedViolation(string text, NeoRuntimeTypeKey parsed, bool closed, bool allowOpaque)
        {
            string? violation = KeyViolation(parsed, allowOpaque, delegateResult: false, nested: false);
            if (violation is not null)
                return $"'{text}' {violation}.";
            if (closed && !IsClosed(parsed))
                return $"'{text}' must be closed, with no type definition or parameter.";
            return null;
        }

        /// <summary>
        /// The first violation of a stored key, or null: <see cref="Violation(string, out NeoRuntimeTypeKey?, bool, bool)"/>
        /// plus the storage rules no stored key may break (§1.3): a
        /// <c>ValueTuple&lt;T&gt;</c> appears only inside a <c>Type</c>
        /// payload, and a Dictionary is keyed by <c>string</c> or an enum.
        /// Only a <c>Type</c> payload (<paramref name="typePayload"/>) may be
        /// open or opaque. Twin of the TypeScript
        /// <c>storedRuntimeTypeKeyViolation</c>.
        /// </summary>
        internal static string? StoredViolation(string text, out NeoRuntimeTypeKey? key, bool typePayload = false)
        {
            if (!TryParse(text, out NeoRuntimeTypeKey parsed, out string error))
            {
                key = null;
                return $"'{text}' is not a runtime type key: {error}.";
            }
            key = parsed;
            string? storage = StorageViolation(parsed, typePayload);
            if (storage is not null)
                return $"'{text}' {storage}.";
            return ParsedViolation(text, parsed, closed: !typePayload, allowOpaque: typePayload);
        }

        /// <inheritdoc cref="StoredViolation(string, out NeoRuntimeTypeKey?, bool)"/>
        internal static string? StoredViolation(string text, bool typePayload = false) =>
            StoredViolation(text, out _, typePayload);

        private static string? StorageViolation(NeoRuntimeTypeKey key, bool typePayload)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.ValueTuple:
                    if (!typePayload)
                        return $"uses '{Spell(key)}', which only a Type payload may name";
                    return StorageViolation(key.Arguments![0], typePayload);
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    if (key.Name == "Dictionary"
                        && key.Arguments!.Length > 0
                        && !IsDictionaryKeyType(key.Arguments[0], typePayload))
                    {
                        return $"keys a Dictionary by '{Spell(key.Arguments[0])}'; Dictionary keys are string or an enum";
                    }
                    return FirstStorageViolation(key.Arguments, typePayload);
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                case NeoRuntimeTypeKeyKind.Tuple:
                case NeoRuntimeTypeKeyKind.Nullable:
                    return FirstStorageViolation(key.Arguments, typePayload);
                default:
                    return null;
            }
        }

        private static string? FirstStorageViolation(NeoRuntimeTypeKey[]? keys, bool typePayload)
        {
            if (keys is null)
                return null;
            foreach (NeoRuntimeTypeKey key in keys)
            {
                string? violation = StorageViolation(key, typePayload);
                if (violation is not null)
                    return violation;
            }
            return null;
        }

        private static bool IsDictionaryKeyType(NeoRuntimeTypeKey key, bool typePayload) => key.Kind switch
        {
            NeoRuntimeTypeKeyKind.Enum => true,
            NeoRuntimeTypeKeyKind.Builtin => key.Name == "string",
            NeoRuntimeTypeKeyKind.Parameter => typePayload,
            _ => false,
        };

        /// <summary>
        /// Why <paramref name="text"/> can't key a box beyond
        /// <see cref="StoredViolation(string, out NeoRuntimeTypeKey?, bool)"/>,
        /// or null (§1.3): no top-level <c>?</c>, no interface,
        /// <c>ValueType</c> or <c>Enum</c>, and no action at any depth. Twin
        /// of the TypeScript <c>boxKeyViolation</c>.
        /// </summary>
        internal static string? BoxKeyViolation(string text, NeoRuntimeTypeKey key)
        {
            if (key.Kind == NeoRuntimeTypeKeyKind.Nullable)
            {
                string underlying = Spell(key.Arguments![0]);
                return $"valueType '{text}' has a top-level '?'; a non-null '{underlying}?' stores its '{underlying}' payload with that key";
            }
            if (key.Kind == NeoRuntimeTypeKeyKind.Interface)
                return $"valueType '{text}' names an interface; a box names its value's own type";
            if (key.Kind == NeoRuntimeTypeKeyKind.Builtin && key.Name is "ValueType" or "Enum")
                return $"valueType '{text}' names '{key.Name}'; a box names its value's own type";
            if (NamesAction(key))
                return $"valueType '{text}' names an action, which isn't a value";
            return null;
        }

        private static bool NamesAction(NeoRuntimeTypeKey key)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Builtin:
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    if (key.Name == NeoAction)
                        return true;
                    break;
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                case NeoRuntimeTypeKeyKind.Tuple:
                case NeoRuntimeTypeKeyKind.Nullable:
                    break;
                default:
                    return false;
            }
            if (key.Arguments is null)
                return false;
            foreach (NeoRuntimeTypeKey argument in key.Arguments)
            {
                if (NamesAction(argument))
                    return true;
            }
            return false;
        }

        /// <summary>Whether a key names no definition and no type parameter at any depth.</summary>
        internal static bool IsClosed(NeoRuntimeTypeKey key)
        {
            if (key.Kind == NeoRuntimeTypeKeyKind.Definition || key.Kind == NeoRuntimeTypeKeyKind.Parameter)
                return false;
            if (key.Arguments is null)
                return true;
            foreach (NeoRuntimeTypeKey argument in key.Arguments)
            {
                if (!IsClosed(argument))
                    return false;
            }
            return true;
        }

        /// <summary>The canonical spelling of a runtime type key; the inverse of the parser.</summary>
        internal static string Spell(NeoRuntimeTypeKey key)
        {
            var builder = new StringBuilder();
            Append(builder, key);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, NeoRuntimeTypeKey key)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Builtin:
                    builder.Append(key.Name);
                    return;
                case NeoRuntimeTypeKeyKind.Void:
                    builder.Append(VoidName);
                    return;
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    builder.Append(key.Name);
                    AppendList(builder, key.Arguments!, '<', '>');
                    return;
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                case NeoRuntimeTypeKeyKind.Enum:
                    builder.Append(Prefix(key.Kind)).Append(key.Id);
                    if (key.Arguments is not null)
                        AppendList(builder, key.Arguments, '<', '>');
                    return;
                case NeoRuntimeTypeKeyKind.Tuple:
                    AppendList(builder, key.Arguments!, '(', ')');
                    return;
                case NeoRuntimeTypeKeyKind.ValueTuple:
                    builder.Append(ValueTupleName);
                    AppendList(builder, key.Arguments!, '<', '>');
                    return;
                case NeoRuntimeTypeKeyKind.Nullable:
                    Append(builder, key.Arguments![0]);
                    builder.Append('?');
                    return;
                case NeoRuntimeTypeKeyKind.Definition:
                    AppendOwner(builder, key);
                    builder.Append('<').Append(',', key.Number - 1).Append('>');
                    return;
                case NeoRuntimeTypeKeyKind.Parameter:
                    builder.Append('!');
                    AppendOwner(builder, key);
                    builder.Append('/').Append(key.Number);
                    return;
                case NeoRuntimeTypeKeyKind.Opaque:
                    builder.Append("opaque:").Append(key.Name);
                    return;
            }
        }

        private static void AppendList(StringBuilder builder, NeoRuntimeTypeKey[] keys, char open, char close)
        {
            builder.Append(open);
            for (int index = 0; index < keys.Length; index++)
            {
                if (index > 0)
                    builder.Append(',');
                Append(builder, keys[index]);
            }
            builder.Append(close);
        }

        private static void AppendOwner(StringBuilder builder, NeoRuntimeTypeKey key)
        {
            if (key.OwnerKind == NeoRuntimeTypeKeyKind.Builtin)
                builder.Append(key.Name);
            else
                builder.Append(Prefix(key.OwnerKind)).Append(key.Id);
        }

        private static string OwnerSpelling(NeoRuntimeTypeKey key)
        {
            var builder = new StringBuilder();
            AppendOwner(builder, key);
            return builder.ToString();
        }

        private static string Prefix(NeoRuntimeTypeKeyKind kind) => kind switch
        {
            NeoRuntimeTypeKeyKind.Class => "c:",
            NeoRuntimeTypeKeyKind.Interface => "i:",
            _ => "e:",
        };

        private static string KindName(NeoRuntimeTypeKeyKind kind) => kind switch
        {
            NeoRuntimeTypeKeyKind.Class => "class",
            NeoRuntimeTypeKeyKind.Interface => "interface",
            _ => "enum",
        };

        private static bool IsValueType(NeoRuntimeTypeKey key) => key.Kind switch
        {
            NeoRuntimeTypeKeyKind.Builtin => BuiltinValueTypes.Contains(key.Name!),
            NeoRuntimeTypeKeyKind.Enum => true,
            NeoRuntimeTypeKeyKind.Tuple => true,
            NeoRuntimeTypeKeyKind.ValueTuple => true,
            _ => false,
        };

        private static string? ArgumentsViolation(NeoRuntimeTypeKey[]? arguments, int start, bool allowOpaque)
        {
            if (arguments is null)
                return null;
            for (int index = start; index < arguments.Length; index++)
            {
                string? violation = KeyViolation(arguments[index], allowOpaque, delegateResult: false, nested: true);
                if (violation is not null)
                    return violation;
            }
            return null;
        }

        /// <summary>A built-in owner's arity, or the violation naming one with none.</summary>
        private static string? OwnerArityViolation(NeoRuntimeTypeKey key, out int? arity)
        {
            arity = null;
            if (key.OwnerKind != NeoRuntimeTypeKeyKind.Builtin)
                return null;
            if (FixedArityGenerics.TryGetValue(key.Name!, out int fixedArity))
            {
                arity = fixedArity;
                return null;
            }
            if (key.Name == NullableDefinition)
            {
                arity = 1;
                return null;
            }
            return $"names '{key.Name}', which has no fixed-arity type parameters";
        }

        private static string? KeyViolation(NeoRuntimeTypeKey key, bool allowOpaque, bool delegateResult, bool nested)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Builtin:
                    if (FixedArityGenerics.ContainsKey(key.Name!))
                        return $"uses generic '{key.Name}' without type arguments";
                    if (key.Name == NeoDelegate)
                        return $"uses '{NeoDelegate}' without a result type";
                    if (key.Name == NeoAction)
                        return null;
                    return NonGenericBuiltins.Contains(key.Name!) ? null : $"names unknown built-in type '{key.Name}'";
                case NeoRuntimeTypeKeyKind.Void:
                    return delegateResult ? null : $"uses 'void' outside a {NeoDelegate} result";
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    return ConstructionViolation(key, allowOpaque);
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                case NeoRuntimeTypeKeyKind.Enum:
                    if (key.Kind == NeoRuntimeTypeKeyKind.Enum && key.Arguments is not null)
                        return $"gives enum '{key.Id}' type arguments";
                    return ArgumentsViolation(key.Arguments, 0, allowOpaque);
                case NeoRuntimeTypeKeyKind.Tuple:
                    return ArgumentsViolation(key.Arguments, 0, allowOpaque);
                case NeoRuntimeTypeKeyKind.ValueTuple:
                    return KeyViolation(key.Arguments![0], allowOpaque, delegateResult: false, nested: true);
                case NeoRuntimeTypeKeyKind.Nullable:
                    {
                        NeoRuntimeTypeKey underlying = key.Arguments![0];
                        if (!IsValueType(underlying))
                            return $"puts '?' after '{Spell(underlying)}', which is not a value type";
                        return KeyViolation(underlying, allowOpaque, delegateResult: false, nested: true);
                    }
                case NeoRuntimeTypeKeyKind.Definition:
                    return DefinitionViolation(key, nested);
                case NeoRuntimeTypeKeyKind.Parameter:
                    {
                        string? violation = OwnerArityViolation(key, out int? arity);
                        if (violation is not null)
                            return violation;
                        if (arity is { } ownerArity && key.Number >= ownerArity)
                            return $"names parameter {key.Number} of '{OwnerSpelling(key)}', which has {ownerArity}";
                        return null;
                    }
                default:
                    if (!allowOpaque)
                        return "uses an opaque type outside a Type payload";
                    return Array.IndexOf(OpaqueTypes, key.Name) >= 0 ? null : $"names unknown opaque type '{key.Name}'";
            }
        }

        private static string? ConstructionViolation(NeoRuntimeTypeKey key, bool allowOpaque)
        {
            string name = key.Name!;
            NeoRuntimeTypeKey[] arguments = key.Arguments!;
            if (name == NeoDelegate)
            {
                if (arguments.Length > NeoDelegateMaxArity)
                    return $"gives {NeoDelegate} more than {NeoDelegateMaxArity} type arguments";
                string? resultViolation = KeyViolation(arguments[0], allowOpaque, delegateResult: true, nested: true);
                return resultViolation ?? ArgumentsViolation(arguments, 1, allowOpaque);
            }
            if (name == NeoAction)
            {
                if (arguments.Length > NeoActionMaxArity)
                    return $"gives {NeoAction} more than {NeoActionMaxArity} type arguments";
                return ArgumentsViolation(arguments, 0, allowOpaque);
            }
            if (name == NullableDefinition)
                return "spells a closed Nullable construction; write the '?' form";
            if (!FixedArityGenerics.TryGetValue(name, out int arity))
            {
                return NonGenericBuiltins.Contains(name)
                    ? $"gives non-generic '{name}' type arguments"
                    : $"names unknown built-in type '{name}'";
            }
            if (arguments.Length != arity)
                return $"gives '{name}' {arguments.Length} type arguments, but it takes {arity}";
            return ArgumentsViolation(arguments, 0, allowOpaque);
        }

        private static string? DefinitionViolation(NeoRuntimeTypeKey key, bool nested)
        {
            if (nested)
                return $"nests definition '{Spell(key)}' inside another key";
            int arity = key.Number;
            if (key.OwnerKind == NeoRuntimeTypeKeyKind.Builtin)
            {
                switch (key.Name)
                {
                    case ValueTupleName:
                        return arity <= ValueTupleMaxArity ? null : $"defines ValueTuple with {arity} parameters, above {ValueTupleMaxArity}";
                    case NullableDefinition:
                        return arity == 1 ? null : $"defines Nullable with {arity} parameters";
                    case NeoDelegate:
                        return arity <= NeoDelegateMaxArity ? null : $"defines {NeoDelegate} with more than {NeoDelegateMaxArity} parameters";
                    case NeoAction:
                        return arity <= NeoActionMaxArity ? null : $"defines {NeoAction} with more than {NeoActionMaxArity} parameters";
                }
            }
            string? violation = OwnerArityViolation(key, out int? ownerArity);
            if (violation is not null)
                return violation;
            if (ownerArity is { } expected && expected != arity)
                return $"defines '{OwnerSpelling(key)}' with {arity} parameters, but it takes {expected}";
            return null;
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            internal Parser(string text)
            {
                this.text = text;
            }

            internal NeoRuntimeTypeKey Parse()
            {
                NeoRuntimeTypeKey key = ParseKey(asArgument: false);
                if (index != text.Length)
                    throw new FormatException($"unexpected '{text[index]}' at {index}");
                return key;
            }

            private NeoRuntimeTypeKey ParseKey(bool asArgument)
            {
                NeoRuntimeTypeKey baseKey = ParseBase(asArgument);
                if (Peek() != '?')
                    return baseKey;
                index++;
                if (Peek() == '?')
                    throw new FormatException($"a second '?' at {index}");
                return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Nullable, Arguments = new[] { baseKey } };
            }

            private NeoRuntimeTypeKey ParseBase(bool asArgument)
            {
                char? next = Peek();
                if (next == '(')
                    return ParseTuple();
                if (next == '!')
                    return ParseParameter();
                if (string.CompareOrdinal(text, index, "opaque:", 0, 7) == 0)
                {
                    index += 7;
                    string opaque = ReadName();
                    if (opaque.Length == 0)
                        throw new FormatException("an empty opaque type name");
                    return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Opaque, Name = opaque };
                }
                NeoRuntimeTypeKeyKind? named = ReadNamedPrefix();
                if (named is { } namedKind)
                {
                    string id = ReadName();
                    if (id.Length == 0)
                        throw new FormatException($"an empty {KindName(namedKind)} id at {index}");
                    if (Peek() != '<')
                        return new NeoRuntimeTypeKey { Kind = namedKind, Id = id };
                    NeoRuntimeTypeKey[]? namedArguments = ParseArgumentList(out int namedArity);
                    if (namedArguments is null)
                    {
                        if (namedKind == NeoRuntimeTypeKeyKind.Enum)
                            throw new FormatException("an enum can't be generic");
                        return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Definition, OwnerKind = namedKind, Id = id, Number = namedArity };
                    }
                    return new NeoRuntimeTypeKey { Kind = namedKind, Id = id, Arguments = namedArguments };
                }
                string name = ReadName();
                if (name.Length == 0)
                    throw new FormatException(next is null ? "an empty key" : $"unexpected '{next}' at {index}");
                if (Peek() != '<')
                {
                    if (name == VoidName && asArgument)
                        return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Void };
                    return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Builtin, Name = name };
                }
                NeoRuntimeTypeKey[]? arguments = ParseArgumentList(out int arity);
                if (arguments is null)
                    return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Definition, OwnerKind = NeoRuntimeTypeKeyKind.Builtin, Name = name, Number = arity };
                if (name == ValueTupleName)
                {
                    if (arguments.Length != 1)
                        throw new FormatException("a closed ValueTuple construction must have one type argument");
                    return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.ValueTuple, Arguments = arguments };
                }
                return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.BuiltinConstruction, Name = name, Arguments = arguments };
            }

            private NeoRuntimeTypeKey ParseTuple()
            {
                index++;
                var elements = new List<NeoRuntimeTypeKey> { ParseKey(asArgument: false) };
                while (Peek() == ',')
                {
                    index++;
                    elements.Add(ParseKey(asArgument: false));
                }
                Expect(')');
                if (elements.Count < 2)
                    throw new FormatException("a tuple needs at least two elements");
                return new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Tuple, Arguments = elements.ToArray() };
            }

            private NeoRuntimeTypeKey ParseParameter()
            {
                index++;
                var key = new NeoRuntimeTypeKey { Kind = NeoRuntimeTypeKeyKind.Parameter };
                NeoRuntimeTypeKeyKind? named = ReadNamedPrefix();
                if (named == NeoRuntimeTypeKeyKind.Enum)
                    throw new FormatException("an enum has no type parameters");
                if (named is { } namedKind)
                {
                    key.OwnerKind = namedKind;
                    key.Id = ReadName();
                    if (key.Id.Length == 0)
                        throw new FormatException($"an empty {KindName(namedKind)} id");
                }
                else
                {
                    key.OwnerKind = NeoRuntimeTypeKeyKind.Builtin;
                    key.Name = ReadName();
                    if (key.Name.Length == 0)
                        throw new FormatException("an empty parameter owner");
                }
                Expect('/');
                int start = index;
                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                    index++;
                int length = index - start;
                if (length == 0)
                    throw new FormatException("a parameter needs an index");
                string digits = text.Substring(start, length);
                if (length > 1 && digits[0] == '0')
                    throw new FormatException($"a non-canonical parameter index '{digits}'");
                if (!int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out key.Number))
                    throw new FormatException($"a parameter index '{digits}' out of range");
                return key;
            }

            /// <summary><c>&lt;a,b&gt;</c>, or null with a definition's arity for <c>&lt;,,&gt;</c>.</summary>
            private NeoRuntimeTypeKey[]? ParseArgumentList(out int definitionArity)
            {
                Expect('<');
                definitionArity = 0;
                if (Peek() == '>' || Peek() == ',')
                {
                    definitionArity = 1;
                    while (Peek() == ',')
                    {
                        index++;
                        definitionArity++;
                    }
                    Expect('>');
                    return null;
                }
                var arguments = new List<NeoRuntimeTypeKey> { ParseKey(asArgument: true) };
                while (Peek() == ',')
                {
                    index++;
                    arguments.Add(ParseKey(asArgument: true));
                }
                Expect('>');
                return arguments.ToArray();
            }

            private NeoRuntimeTypeKeyKind? ReadNamedPrefix()
            {
                if (index + 1 >= text.Length || text[index + 1] != ':')
                    return null;
                NeoRuntimeTypeKeyKind? kind = text[index] switch
                {
                    'c' => NeoRuntimeTypeKeyKind.Class,
                    'i' => NeoRuntimeTypeKeyKind.Interface,
                    'e' => NeoRuntimeTypeKeyKind.Enum,
                    _ => null,
                };
                if (kind is not null)
                    index += 2;
                return kind;
            }

            private string ReadName()
            {
                int start = index;
                while (index < text.Length && !IsDelimiter(text[index]))
                    index++;
                return text.Substring(start, index - start);
            }

            private static bool IsDelimiter(char character) =>
                character is '<' or '>' or ',' or '(' or ')' or '?' or '/' or '!' || char.IsWhiteSpace(character);

            private char? Peek() => index < text.Length ? text[index] : null;

            private void Expect(char character)
            {
                if (Peek() == character)
                {
                    index++;
                    return;
                }
                throw new FormatException(index < text.Length
                    ? $"expected '{character}' at {index}, found '{text[index]}'"
                    : $"expected '{character}' at {index} (end of key)");
            }
        }
    }
}
