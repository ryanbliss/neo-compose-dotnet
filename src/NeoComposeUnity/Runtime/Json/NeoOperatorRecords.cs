// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace NeoCompose.Runtime.Json
{
    /// <summary>The two stored-operator validation stages in B5 contract 17.2.</summary>
    internal static class NeoOperatorRecords
    {
        private static readonly HashSet<string> Unary = new("op_UnaryPlus op_UnaryNegation op_CheckedUnaryNegation op_LogicalNot op_OnesComplement op_Increment op_CheckedIncrement op_Decrement op_CheckedDecrement op_True op_False".Split(' '));
        private static readonly HashSet<string> Binary = new("op_Addition op_CheckedAddition op_Subtraction op_CheckedSubtraction op_Multiply op_CheckedMultiply op_Division op_CheckedDivision op_Modulus op_BitwiseAnd op_BitwiseOr op_ExclusiveOr op_LeftShift op_RightShift op_UnsignedRightShift op_Equality op_Inequality op_LessThan op_GreaterThan op_LessThanOrEqual op_GreaterThanOrEqual".Split(' '));
        private static readonly Dictionary<string, string> Pairs = new()
        {
            ["op_Equality"] = "op_Inequality",
            ["op_Inequality"] = "op_Equality",
            ["op_LessThan"] = "op_GreaterThan",
            ["op_GreaterThan"] = "op_LessThan",
            ["op_LessThanOrEqual"] = "op_GreaterThanOrEqual",
            ["op_GreaterThanOrEqual"] = "op_LessThanOrEqual",
            ["op_True"] = "op_False",
            ["op_False"] = "op_True",
        };
        private static bool True(JToken? value) => value?.Type == JTokenType.Boolean && value.Value<bool>();
        private static string Text(JToken? value) => value?.Type == JTokenType.String ? value.Value<string>()! : "";
        private static int? Integer(JToken? value) => value?.Type == JTokenType.Integer ? value.Value<int>() : null;
        private static JArray Args(JObject row) => row["argumentTypes"] as JArray ?? new JArray();

        internal static void ValidateMember(JObject row)
        {
            if (row.Property("operator") is null)
                return;
            string? error = null;
            if (row.Property("explicitImplementation") is not null && !ValidExplicit(row["explicitImplementation"]))
                error = "explicitImplementation must name an interface, its arguments and an interface member.";
            else if (!True(row["operator"]))
                error = "operator must be true when present.";
            else if (Integer(row["kind"]) != (int)MemberKind.Function && Integer(row["kind"]) != (int)MemberKind.NSFunction)
                error = "operator is only valid on a function member.";
            else
                error = Shape(row, Text(row["name"]), false);
            if (error is not null)
                throw new JsonSerializationException($"Member \"{Text(row["id"])}\": {error}");
        }

        private static bool ValidExplicit(JToken? token)
        {
            if (token is not JObject value || Text(value["interfaceId"]).Length == 0 || Text(value["interfaceMemberId"]).Length == 0)
                return false;
            if (value.Properties().Any(p => p.Name is not ("interfaceId" or "interfaceMemberId" or "arguments")))
                return false;
            if (value.Property("arguments") is null)
                return true;
            return value["arguments"] is JObject arguments && arguments.Properties().All(p => p.Value is JObject);
        }

        private static string? Shape(JObject row, string name, bool inInterface)
        {
            int arity = Unary.Contains(name) ? 1 : Binary.Contains(name) ? 2 : 0;
            if (arity == 0)
                return $"'{name}' is not an operator metadata name.";
            if (!inInterface && row.Property("extendsMemberId") is not null)
                return "An operator can't override a member.";
            if (!(inInterface ? True(row["static"]) : Integer(row["modifier"]) == (int)NeoMemberModifierKind.Static))
                return "User-defined operator must be declared static (CS0558).";
            if (row.Property("access") is not null && Integer(row["access"]) != (int)NeoMemberAccessKind.Public && row.Property("explicitImplementation") is null)
                return "User-defined operator must be declared public (CS0558).";
            if (row["genericParams"] is JArray { Count: > 0 })
                return "An operator can't declare type parameters.";
            var args = Args(row);
            if (args.Count != arity)
                return $"'{name}' takes {arity} parameter(s), not {args.Count}.";
            if (args.Any(arg => arg is JObject obj && Integer(obj["mode"]) == (int)NeoParameterModeKind.Out))
                return "An operator parameter can't be 'out' (CS0631).";
            if (name is "op_True" or "op_False" && !(row["returnTypeInfo"] is JObject result && Integer(result["type"]) == (int)MemberKind.Bool && True(result["required"])))
                return "The return type of operator True or False must be bool (CS0215).";
            return null;
        }

        internal static void ValidateProject(JObject root)
        {
            var classes = root["classes"] as JObject ?? new JObject();
            var members = root["members"] as JObject ?? new JObject();
            foreach (var owner in classes.Properties().Select(p => p.Value).OfType<JObject>())
            {
                var operators = new List<(string Name, JObject Row)>();
                foreach (var entry in (owner["schema"] as JObject ?? new JObject()).Properties())
                    if (members[Text(entry.Value)] is JObject member && True(member["operator"]))
                        operators.Add((Text(member["name"]), member));
                ValidateOwner(owner, operators, false, classes);
            }
            foreach (var owner in (root["interfaces"] as JObject ?? new JObject()).Properties().Select(p => p.Value).OfType<JObject>())
            {
                var operators = (owner["members"] as JObject ?? new JObject()).Properties()
                    .Where(p => p.Value is JObject value && True(value["operator"]))
                    .Select(p => (NeoCallableSignature.Name(p.Name), (JObject)p.Value)).ToList();
                ValidateOwner(owner, operators, true, classes);
            }
        }

        private static void ValidateOwner(JObject owner, List<(string Name, JObject Row)> operators, bool inInterface, JObject classes)
        {
            string id = Text(owner["id"]), ownerName = Text(owner["name"]);
            foreach (var (name, row) in operators)
                if (Shape(row, name, inInterface) is string shape)
                    throw new JsonSerializationException(inInterface
                        ? $"Interface \"{ownerName}\" ({id}) member \"{name}\" ({Text(row["id"])}): {shape}"
                        : $"Member \"{name}\" ({Text(row["id"])}): {shape}");
            if (operators.Count == 0)
                return;
            string prefix = $"{(inInterface ? "Interface" : "Class")} \"{ownerName}\" ({id}) operator";
            string[] parameters = (owner["genericParams"] as JArray ?? new JArray()).OfType<JObject>().Select(p => Text(p["id"])).ToArray();
            string ownerField = inInterface ? "ownerInterfaceId" : "ownerClassId";
            var selfParameters = new HashSet<string>();
            if (inInterface)
                foreach (JObject param in (owner["genericParams"] as JArray ?? new JArray()).OfType<JObject>())
                    if ((param["constraints"] as JArray ?? new JArray()).OfType<JObject>().Any(c => Integer(c["kind"]) == (int)NeoGenericParamConstraintKind.Interface && Text(c["interfaceId"]) == id
                        && parameters.All(p => c["interfaceArguments"]?[p] is JObject a && Integer(a["kind"]) == (int)TypeBindingKind.Open && Text(a["param"]) == p
                            && Integer(a["owner"]?["type"]) == (int)MemberKind.Interface && Text(a["owner"]?["interfaceId"]) == id)))
                        selfParameters.Add(Text(param["id"]));
            bool Own(JToken? token)
            {
                if (token is not JObject type)
                    return false;
                if (inInterface && Integer(type["type"]) == (int)MemberKind.Generic && Text(type[ownerField]) == id)
                    return selfParameters.Contains(Text(type["genericParamId"]));
                if (Integer(type["type"]) != (int)(inInterface ? MemberKind.Interface : MemberKind.Class) || Text(type[inInterface ? "interfaceId" : "classId"]) != id)
                    return false;
                if (parameters.Length == 0)
                    return type.Property("typeArguments") is null;
                return type["typeArguments"] is JObject args && args.Count == parameters.Length && parameters.All(p => args[p] is JObject a
                    && Integer(a["type"]) == (int)MemberKind.Generic && Text(a[ownerField]) == id && Text(a["genericParamId"]) == p);
            }
            bool Subclass(JToken? returned)
            {
                if (returned is not JObject type || Integer(type["type"]) != (int)MemberKind.Class)
                    return false;
                var seen = new HashSet<string>();
                for (string current = Text(type["classId"]); seen.Add(current) && classes[current] is JObject record; current = Text(record["extendsClassId"]))
                    if (current == id)
                        return true;
                return false;
            }
            var signatures = new HashSet<string>();
            string Signature(string name, JObject row) => name + " " + TypeKey(row["returnTypeInfo"]) + "(" + string.Join(",", Args(row).Select(TypeKey)) + ")";
            foreach (var (name, row) in operators)
                if (row.Property("explicitImplementation") is null)
                    signatures.Add(Signature(name, row));
            foreach (var (name, row) in operators)
            {
                var args = Args(row);
                JToken first = args[0];
                JToken? returned = row["returnTypeInfo"];
                string? error = null;
                if (name is "op_Increment" or "op_Decrement" or "op_CheckedIncrement" or "op_CheckedDecrement")
                {
                    if (!Own(first))
                        error = inInterface ? "The parameter type for ++ or -- operator must be the containing type, or its type parameter constrained to it (CS8922)." : "The parameter type for ++ or -- operator must be the containing type (CS0559).";
                    else if (TypeKey(first) != TypeKey(returned) && !(inInterface ? Own(returned) && !(Integer(first["type"]) == (int)MemberKind.Generic && parameters.Contains(Text(first["genericParamId"]))) : Subclass(returned)))
                        error = inInterface ? "The return type for ++ or -- operator must either match the parameter type, or be derived from the parameter type, or be the containing type's type parameter constrained to it unless the parameter type is a different type parameter (CS8923)." : "The return type for ++ or -- operator must match the parameter type or be derived from the parameter type (CS0448).";
                }
                else if (args.Count == 1 && !Own(first))
                    error = inInterface ? "The parameter of a unary operator must be the containing type, or its type parameter constrained to it (CS8921)." : "The parameter of a unary operator must be the containing type (CS0562).";
                else if (name is "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift")
                {
                    if (!Own(first))
                        error = inInterface ? "The first operand of an overloaded shift operator must have the same type as the containing type or its type parameter constrained to it (CS8925)." : "The first operand of an overloaded shift operator must have the same type as the containing type (CS0564).";
                }
                else if (args.Count == 2 && !Own(first) && !Own(args[1]))
                    error = inInterface ? "One of the parameters of a binary operator must be the containing type, or its type parameter constrained to it (CS8924)." : "One of the parameters of a binary operator must be the containing type (CS0563).";
                if (error is not null)
                    throw new JsonSerializationException($"{prefix} '{name}': {error}");
                if (row.Property("explicitImplementation") is not null)
                    continue;
                if (Pairs.TryGetValue(name, out string paired) && !signatures.Contains(Signature(paired, row)))
                    throw new JsonSerializationException($"{prefix} The operator '{name}' requires a matching operator '{paired}' to also be defined (CS0216).");
                if (name.StartsWith("op_Checked", StringComparison.Ordinal) && !signatures.Contains(Signature("op_" + name.Substring(10), row)))
                    throw new JsonSerializationException($"{prefix} The operator '{name}' requires a matching non-checked version of the operator to also be defined (CS9025).");
            }
        }

        private static string TypeKey(JToken? token)
        {
            if (token is null)
                return "undefined";
            JToken Clean(JToken value, bool top)
            {
                if (value is JArray array)
                    return new JArray(array.Select(v => Clean(v, false)));
                if (value is not JObject obj)
                    return value.DeepClone();
                int? kind = Integer(obj["type"]);
                bool isValue = kind is (int)MemberKind.Bool or (int)MemberKind.Int or (int)MemberKind.Float or (int)MemberKind.Decimal or (int)MemberKind.Enum or (int)MemberKind.Tuple or (int)MemberKind.Vector2 or (int)MemberKind.Vector2Int or (int)MemberKind.Vector3 or (int)MemberKind.Vector3Int or (int)MemberKind.Color;
                var result = new JObject();
                foreach (var p in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (p.Name is "listMemberId" or "setMemberId" or "name" or "defaultValue" || top && (p.Name is "mode" or "flow" || p.Name == "required" && !isValue))
                        continue;
                    result[p.Name] = p.Name == "type" && kind == (int)MemberKind.Set ? new JValue((int)MemberKind.Lookup) : Clean(p.Value, false);
                }
                return result;
            }
            return Clean(token, true).ToString(Formatting.None);
        }
    }
}
