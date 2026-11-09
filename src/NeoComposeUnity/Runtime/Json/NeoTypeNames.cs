// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// A type binding spelled as NeoScript and C# spell it, the way P127 §2's
    /// <c>Type.Name</c> prints: <c>int</c>, <c>int?</c>, <c>List&lt;int&gt;</c>,
    /// <c>Box&lt;Money&gt;</c>, <c>(int, string)</c>, <c>object</c>. A
    /// definition prints its parameter names (<c>Box&lt;T&gt;</c>) and a
    /// parameter prints its name. Declared types print their declared names;
    /// without a project, where only ids are known, they print their ids.
    /// Unlike <c>Type.Name</c>, a nullable reference keeps its <c>?</c>, since
    /// refusals name the authored type.
    /// </summary>
    internal static class NeoTypeNames
    {
        internal static string Of(TypeBinding binding, ProjectData? data = null)
        {
            var builder = new StringBuilder();
            Append(builder, binding, data);
            return builder.ToString();
        }

        /// <summary>A head's name without its type arguments or <c>?</c>: <c>List</c>, <c>Box</c>, <c>int</c>.</summary>
        internal static string Head(TypeHead head, ProjectData? data = null)
        {
            if (head.IsOpaque)
                return head.OpaqueName!;
            return head.Type switch
            {
                MemberKind.Class => ClassName(head.ClassId!, data),
                MemberKind.Interface => InterfaceName(head.InterfaceId!, data),
                MemberKind.Enum => EnumName(head.EnumId!, data),
                MemberKind.Object => ObjectName(head.ObjectType),
                MemberKind.Tuple => "ValueTuple",
                _ => LeafName(head.Type),
            };
        }

        private static void Append(StringBuilder builder, TypeBinding binding, ProjectData? data)
        {
            switch (binding.Kind)
            {
                case TypeBindingKind.Open:
                    builder.Append(ParameterName(binding, data));
                    return;
                case TypeBindingKind.MethodParam:
                    builder.Append("T").Append((binding.Index!.Value + 1).ToString(CultureInfo.InvariantCulture));
                    return;
                case TypeBindingKind.Definition:
                    AppendDefinition(builder, binding.Definition!, data);
                    return;
            }
            TypeHead head = binding.Head!;
            IReadOnlyDictionary<string, TypeBinding>? bindings = binding.Bindings;
            if (head.Type == MemberKind.Tuple && bindings is { Count: > 1 })
            {
                AppendPositions(builder, bindings, data, '(', ')');
            }
            else
            {
                builder.Append(Head(head, data));
                if (bindings is { Count: > 0 })
                {
                    List<GenericParamDeclaration>? parameters = DeclaredParameters(head, data);
                    if (parameters is null)
                        AppendPositions(builder, bindings, data, '<', '>');
                    else
                        AppendDeclared(builder, bindings, parameters, data);
                }
            }
            if (!head.Required)
                builder.Append('?');
        }

        /// <summary>A built-in head's arguments by position, "0" first.</summary>
        private static void AppendPositions(StringBuilder builder, IReadOnlyDictionary<string, TypeBinding> bindings, ProjectData? data, char open, char close)
        {
            builder.Append(open);
            for (int index = 0; index < bindings.Count; index++)
            {
                if (index > 0)
                    builder.Append(", ");
                if (bindings.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out TypeBinding? argument))
                    Append(builder, argument, data);
                else
                    builder.Append('?');
            }
            builder.Append(close);
        }

        /// <summary>A declared head's arguments in its declared parameter order.</summary>
        private static void AppendDeclared(
            StringBuilder builder,
            IReadOnlyDictionary<string, TypeBinding> bindings,
            List<GenericParamDeclaration> parameters,
            ProjectData? data)
        {
            builder.Append('<');
            bool first = true;
            foreach (GenericParamDeclaration parameter in parameters)
            {
                if (!first)
                    builder.Append(", ");
                first = false;
                if (bindings.TryGetValue(parameter.id, out TypeBinding? argument))
                    Append(builder, argument, data);
                else
                    builder.Append(parameter.name);
            }
            builder.Append('>');
        }

        private static void AppendDefinition(StringBuilder builder, TypeDefinition definition, ProjectData? data)
        {
            if (definition.Type is MemberKind.Class or MemberKind.Interface && !definition.Nullable)
            {
                bool isClass = definition.Type == MemberKind.Class;
                string id = (isClass ? definition.ClassId : definition.InterfaceId)!;
                builder.Append(isClass ? ClassName(id, data) : InterfaceName(id, data));
                List<GenericParamDeclaration>? parameters = isClass ? ClassParameters(id, data) : InterfaceParameters(id, data);
                if (parameters is not { Count: > 0 })
                    return;
                var names = new string[parameters.Count];
                for (int index = 0; index < names.Length; index++)
                    names[index] = parameters[index].name;
                AppendNames(builder, names);
                return;
            }
            builder.Append(definition.Nullable ? "Nullable" : definition.Type == MemberKind.Tuple ? "ValueTuple" : LeafName(definition.Type));
            var builtInNames = new string[definition.BuiltInArity ?? 0];
            for (int index = 0; index < builtInNames.Length; index++)
                builtInNames[index] = BuiltInParameterName(definition, index);
            AppendNames(builder, builtInNames);
        }

        internal static string BuiltInParameterName(TypeDefinition owner, int index)
        {
            if (owner.BuiltInArity is not int count || index < 0 || index >= count)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            if (owner.Nullable)
                return "T";
            return owner.Type switch
            {
                MemberKind.Dictionary => index == 0 ? "TKey" : "TValue",
                MemberKind.Tuple when count == 8 && index == 7 => "TRest",
                MemberKind.Tuple or MemberKind.NSAction => "T" + (index + 1).ToString(CultureInfo.InvariantCulture),
                MemberKind.NSDelegate => index == 0 ? "TResult" : "T" + index.ToString(CultureInfo.InvariantCulture),
                _ => "T",
            };
        }

        private static void AppendNames(StringBuilder builder, params string[] names)
        {
            if (names.Length == 0)
                return;
            builder.Append('<').Append(string.Join(", ", names)).Append('>');
        }

        /// <summary>A non-generic built-in's name, shared with <c>NeoReferenceTypes.Format</c>.</summary>
        internal static string LeafName(MemberKind kind) => kind switch
        {
            MemberKind.Int => "int",
            MemberKind.Float => "float",
            MemberKind.Decimal => "decimal",
            MemberKind.Bool => "bool",
            MemberKind.String => "string",
            MemberKind.Void => "void",
            MemberKind.Sprite => "SpriteInfo",
            MemberKind.Audio => "AudioClipInfo",
            MemberKind.DialogueLookup => "NeoDialogueReference",
            MemberKind.NSDelegate => "NeoDelegate",
            MemberKind.NSAction => "NeoAction",
            MemberKind.Variant => "NeoVariant",
            _ => kind.ToString(),
        };

        /// <summary>The server's <c>objectTypeName</c>: a held built-in comparer prints as <c>object</c>.</summary>
        internal static string ObjectName(NeoObjectTypeKind? objectType) => objectType switch
        {
            NeoObjectTypeKind.ValueType => "ValueType",
            NeoObjectTypeKind.Enum => "Enum",
            NeoObjectTypeKind.Type => "Type",
            NeoObjectTypeKind.CultureInfo => "CultureInfo",
            NeoObjectTypeKind.NumberFormatInfo => "NumberFormatInfo",
            _ => "object",
        };

        private static List<GenericParamDeclaration>? DeclaredParameters(TypeHead head, ProjectData? data) => head.Type switch
        {
            MemberKind.Class => ClassParameters(head.ClassId!, data),
            MemberKind.Interface => InterfaceParameters(head.InterfaceId!, data),
            _ => null,
        };

        private static List<GenericParamDeclaration>? ClassParameters(string id, ProjectData? data) =>
            data?.classes is { } classes && classes.TryGetValue(id, out NeoSchemaClass? schemaClass) ? schemaClass.genericParams : null;

        private static List<GenericParamDeclaration>? InterfaceParameters(string id, ProjectData? data) =>
            data?.interfaces is { } interfaces && interfaces.TryGetValue(id, out Interface? record) ? record.genericParams : null;

        internal static string ClassName(string id, ProjectData? data) =>
            data?.classes is { } classes && classes.TryGetValue(id, out NeoSchemaClass? schemaClass) ? schemaClass.name : id;

        internal static string InterfaceName(string id, ProjectData? data) =>
            data?.interfaces is { } interfaces && interfaces.TryGetValue(id, out Interface? record) ? record.name : id;

        internal static string EnumName(string id, ProjectData? data) =>
            data?.enums is { } enums && enums.TryGetValue(id, out Enum? record) ? record.name : id;

        /// <summary>A parameter's name is resolved only within its owning definition.</summary>
        private static string ParameterName(TypeBinding parameter, ProjectData? data)
        {
            TypeDefinition owner = parameter.Definition!;
            if (owner.BuiltInArity is not null)
                return BuiltInParameterName(owner, int.Parse(parameter.Param!, CultureInfo.InvariantCulture));
            List<GenericParamDeclaration>? parameters = owner.Type switch
            {
                MemberKind.Class => ClassParameters(owner.ClassId!, data),
                MemberKind.Interface => InterfaceParameters(owner.InterfaceId!, data),
                _ => null,
            };
            return Find(parameters, parameter.Param!) ?? parameter.Param!;
        }

        private static string? Find(List<GenericParamDeclaration>? parameters, string id)
        {
            if (parameters is null)
                return null;
            foreach (GenericParamDeclaration parameter in parameters)
            {
                if (parameter.id == id)
                    return parameter.name;
            }
            return null;
        }
    }
}
