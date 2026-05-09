// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Runtime
{
    internal static class NeoDialogueActionEvaluator
    {
        internal static void Execute(
            NeoClient client,
            FunctionWithReturnType action,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore = null)
        {
            var ctx = NeoDialogueConditionEvaluator.BuildContext(
                client,
                dialogueContext,
                memoryStore);
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = ctx.thisValue,
                ["__root__"] = ctx.rootValue,
                ["__context__"] = ctx.contextValue,
            };
            ExecuteInstructions(client, action.instructions, scope, ctx);
        }

        private static bool ExecuteInstructions(
            NeoClient client,
            Instruction[] instructions,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            foreach (var instruction in instructions)
            {
                switch (instruction)
                {
                    case VariableInstruction variable:
                        scope[variable.variable.id] = Eval(variable.variable.pointer, scope, ctx);
                        break;
                    case IfInstruction ifInstruction:
                    {
                        bool matched = false;
                        foreach (var branch in ifInstruction.branches)
                        {
                            if (EvaluateBoolean(branch.expression, scope, ctx))
                            {
                                matched = true;
                                if (ExecuteInstructions(client, branch.instructions, scope, ctx))
                                {
                                    return true;
                                }
                                break;
                            }
                        }
                        if (!matched && ifInstruction.elseInstructions != null)
                        {
                            if (ExecuteInstructions(client, ifInstruction.elseInstructions, scope, ctx))
                            {
                                return true;
                            }
                        }
                        break;
                    }
                    case ReturnInstruction:
                        return true;
                    case ThrowInstruction throwInstruction:
                        throw new NSGetterRuntimeError(
                            Eval(throwInstruction.pointer, scope, ctx)?.ToString() ?? "null");
                    case AssignInstruction assign:
                        ExecuteAssign(client, assign, scope, ctx);
                        break;
                    case CollectionCallInstruction collectionCall:
                        ExecuteCollectionCall(client, collectionCall, scope, ctx);
                        break;
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown instruction kind {instruction.GetType().Name}");
                }
            }
            return false;
        }

        private static void ExecuteAssign(
            NeoClient client,
            AssignInstruction instruction,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            object? rhs = Eval(instruction.pointer, scope, ctx);
            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                object? current = scope.TryGetValue(variablePointer.variableId, out var value)
                    ? value
                    : null;
                scope[variablePointer.variableId] = ApplyAssignment(
                    current,
                    rhs,
                    instruction.operatorValue);
                return;
            }

            var target = ResolveTarget(client, instruction.target, scope, ctx);
            object? currentValue = target.ReadCurrentValue(client, ctx);
            object? nextValue = ApplyAssignment(
                currentValue,
                rhs,
                instruction.operatorValue);
            target.Write(client, nextValue);
        }

        private static void ExecuteCollectionCall(
            NeoClient client,
            CollectionCallInstruction instruction,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            object?[] args = new object?[instruction.args.Length];
            for (int i = 0; i < instruction.args.Length; i++)
            {
                args[i] = Eval(instruction.args[i], scope, ctx);
            }

            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                if (!scope.TryGetValue(variablePointer.variableId, out var local))
                {
                    throw new NSGetterRuntimeError(
                        $"Variable '{variablePointer.variableId}' is not in scope");
                }
                MutateLocalCollection(local, instruction.mutation, args);
                scope[variablePointer.variableId] = local;
                return;
            }

            var target = ResolveCollectionTarget(client, instruction.target, scope, ctx);
            target.Mutate(client, instruction.mutation, args, ctx);
        }

        private static object? Eval(
            Pointer pointer,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        }

        private static bool EvaluateBoolean(
            BooleanExpression expression,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            object? result = Eval(new OperationPointer
            {
                type = PointerKind.Operation,
                operation = new BooleanOperation
                {
                    type = OperationKind.Boolean,
                    expression = expression,
                },
            }, scope, ctx);
            if (result is bool b) return b;
            throw new NSGetterRuntimeError("If condition did not evaluate to bool.");
        }

        private static object? ApplyAssignment(
            object? current,
            object? rhs,
            string op)
        {
            switch (op)
            {
                case "=":
                    return rhs;
                case "++":
                    return ToDouble(current, "Increment target") + 1;
                case "--":
                    return ToDouble(current, "Decrement target") - 1;
                case "+=":
                    if (current is string || rhs is string)
                    {
                        return $"{current}{rhs}";
                    }
                    return ToDouble(current, "Left side") + ToDouble(rhs, "Right side");
                case "-=":
                    return ToDouble(current, "Left side") - ToDouble(rhs, "Right side");
                case "*=":
                    return ToDouble(current, "Left side") * ToDouble(rhs, "Right side");
                case "/=":
                    return ToDouble(current, "Left side") / ToDouble(rhs, "Right side");
                case "%=":
                    return ToDouble(current, "Left side") % ToDouble(rhs, "Right side");
                default:
                    throw new NSGetterRuntimeError($"Unknown assignment operator '{op}'.");
            }
        }

        private static NeoResolvedWriteTarget ResolveTarget(
            NeoClient client,
            WriteTarget target,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            EnsureWritable(target);
            switch (target.pointer)
            {
                case ReferencePointer reference:
                    return new NeoRowWriteTarget(reference.valueId, target.typeInfo);
                case KeyOfPointer keyOfPointer:
                    return ResolveKeyOfTarget(client, keyOfPointer.keyOf, target.typeInfo, scope, ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unsupported assignment target '{target.pointer.GetType().Name}'.");
            }
        }

        private static NeoResolvedCollectionTarget ResolveCollectionTarget(
            NeoClient client,
            WriteTarget target,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            EnsureWritable(target);
            object? value = Eval(target.pointer, scope, ctx);
            string? rowId = FindValueId(value, ctx);
            if (rowId == null)
            {
                throw new NSGetterRuntimeError("Collection mutation target is not backed by a Neo value row.");
            }
            EnsureSaveRow(client, rowId);
            if (!client.TryGetValue(rowId, out AttributeValue? row))
            {
                throw new NSGetterRuntimeError($"Missing collection row '{rowId}'.");
            }
            if (row is ArrayAttributeValue)
            {
                if (target.typeInfo is LookupTypeInfo lookupTypeInfo)
                {
                    return new NeoLookupSetWriteTarget(rowId, lookupTypeInfo);
                }
                return new NeoListWriteTarget(rowId, EntryTypeInfo(target.typeInfo));
            }
            if (row is ObjectAttributeValue)
            {
                return new NeoDictionaryWriteTarget(rowId, EntryTypeInfo(target.typeInfo));
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static NeoResolvedWriteTarget ResolveKeyOfTarget(
            NeoClient client,
            KeyOf keyOf,
            TypeInfo targetType,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            object? receiver = Eval(keyOf.pointer, scope, ctx);
            object? key = Eval(keyOf.key, scope, ctx);
            string? receiverRowId = FindValueId(receiver, ctx);
            if (receiverRowId == null)
            {
                throw new NSGetterRuntimeError("Assignment receiver is not backed by a Neo value row.");
            }
            EnsureSaveRow(client, receiverRowId);
            if (!client.TryGetValue(receiverRowId, out AttributeValue? row))
            {
                throw new NSGetterRuntimeError($"Missing receiver row '{receiverRowId}'.");
            }

            if (row is ArrayAttributeValue)
            {
                return new NeoListIndexWriteTarget(receiverRowId, ToInt(key, "List assignment index"), targetType);
            }
            if (row is ObjectAttributeValue objectRow)
            {
                string keyString = ToStringKey(key, "Dictionary/custom assignment key");
                if (!string.IsNullOrEmpty(objectRow.typeId)
                    && TryResolveCustomMemberAttribute(client, objectRow.typeId!, keyString, out JsonAttribute? memberAttribute))
                {
                    return new NeoCustomMemberWriteTarget(receiverRowId, keyString, memberAttribute!);
                }
                return new NeoDictionaryEntryWriteTarget(receiverRowId, keyString, targetType);
            }
            throw new NSGetterRuntimeError("Assignment receiver must be a list, dictionary, or custom object.");
        }

        private static void EnsureWritable(WriteTarget target)
        {
            if (target.writability == WritabilityKind.Asset
                || target.writability == WritabilityKind.ReadOnly)
            {
                throw new NSGetterRuntimeError("Cannot mutate read-only dialogue action target.");
            }
        }

        private static void EnsureSaveRow(NeoClient client, string rowId)
        {
            if (!client.saveValues.ContainsKey(rowId))
            {
                if (!TryMaterializeSavePath(client, rowId))
                {
                    throw new NSGetterRuntimeError(
                        $"Cannot mutate value '{rowId}' because it is not save-owned.");
                }
            }
        }

        private static bool TryMaterializeSavePath(NeoClient client, string rowId)
        {
            string? rootValueId = client.save.value?.id;
            if (string.IsNullOrEmpty(rootValueId)) return false;

            var path = new List<string>();
            if (!TryFindValuePath(client, rootValueId!, rowId, new HashSet<string>(), path))
            {
                return false;
            }

            for (int i = 0; i < path.Count; i++)
            {
                string pathValueId = path[i];
                if (client.saveValues.ContainsKey(pathValueId)) continue;
                if (!client.TryGetValue(pathValueId, out AttributeValue? row)) return false;

                var clone = CloneValueRow(row);
                if (i == 0)
                {
                    client.AddSaveValue(client.project.rootSaveFileAttributeId, clone);
                }
                else
                {
                    client.SetSaveValueSilently(clone);
                }
            }
            return true;
        }

        private static bool TryFindValuePath(
            NeoClient client,
            string currentValueId,
            string targetValueId,
            HashSet<string> visited,
            List<string> path)
        {
            if (!visited.Add(currentValueId)) return false;
            path.Add(currentValueId);
            if (currentValueId == targetValueId) return true;

            if (client.TryGetValue(currentValueId, out AttributeValue? row))
            {
                switch (row)
                {
                    case ObjectAttributeValue obj when obj.value != null:
                        foreach (var childValueId in obj.value.Values)
                        {
                            if (TryFindValuePath(client, childValueId, targetValueId, visited, path))
                            {
                                return true;
                            }
                        }
                        break;
                    case ArrayAttributeValue arr when arr.value != null:
                        foreach (var childValueId in arr.value)
                        {
                            if (TryFindValuePath(client, childValueId, targetValueId, visited, path))
                            {
                                return true;
                            }
                        }
                        break;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static AttributeValue CloneValueRow(AttributeValue row)
        {
            AttributeValue clone = row switch
            {
                NullAttributeValue n => new NullAttributeValue { value = n.value },
                BoolAttributeValue b => new BoolAttributeValue { value = b.value },
                NumberAttributeValue n => new NumberAttributeValue { value = n.value },
                StringAttributeValue s => new StringAttributeValue { value = s.value },
                ArrayAttributeValue a => new ArrayAttributeValue
                {
                    value = a.value == null ? null : (string[])a.value.Clone(),
                },
                ObjectAttributeValue o => new ObjectAttributeValue
                {
                    value = o.value == null ? null : new Dictionary<string, string>(o.value),
                },
                _ => throw new NSGetterRuntimeError(
                    $"Unsupported save value row type '{row.GetType().Name}'."),
            };
            clone.id = row.id;
            clone.createdAt = row.createdAt;
            clone.updatedAt = row.updatedAt;
            clone.typeId = row.typeId;
            return clone;
        }

        private static bool TryResolveCustomMemberAttribute(
            NeoClient client,
            string customTypeId,
            string key,
            out JsonAttribute? attribute)
        {
            attribute = null;
            IList<MergedSchemaEntry> merged;
            try
            {
                merged = CustomTypeInheritance.MergeSchemas(
                    CustomTypeInheritance.ResolveChain(
                        customTypeId,
                        id => client.TryGetType(id, out CustomType? type) ? type : null));
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            foreach (var entry in merged)
            {
                if (entry.schemaKey != key) continue;
                return client.TryGetAttribute(entry.attributeId, out attribute);
            }
            return false;
        }

        private static string? FindValueId(
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                return reference.valueId;
            }
            return NSGetterEvaluator.FindRowIdByReference(value, ctx);
        }

        private static bool TryGetCustomValueReferenceId(
            object? value,
            TypeInfo typeInfo,
            out string? valueId)
        {
            valueId = null;
            if (typeInfo.type != AttributeType.Custom) return false;
            if (value is not INeoValueReference reference
                || string.IsNullOrEmpty(reference.valueId))
            {
                return false;
            }
            valueId = reference.valueId;
            return true;
        }

        private static TypeInfo AttributeTypeInfo(JsonAttribute attribute)
        {
            return attribute switch
            {
                NullAttribute => new PrimitiveTypeInfo { type = AttributeType.Null, required = attribute.required },
                BoolAttribute => new PrimitiveTypeInfo { type = AttributeType.Bool, required = attribute.required },
                IntAttribute => new PrimitiveTypeInfo { type = AttributeType.Int, required = attribute.required },
                FloatAttribute => new PrimitiveTypeInfo { type = AttributeType.Float, required = attribute.required },
                StringAttribute => new PrimitiveTypeInfo { type = AttributeType.String, required = attribute.required },
                CustomAttribute custom => new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = attribute.required,
                    typeId = custom.customTypeId,
                },
                EnumAttribute enumAttribute => new EnumTypeInfo
                {
                    type = AttributeType.Enum,
                    required = attribute.required,
                    enumId = enumAttribute.enumId,
                },
                _ => new PrimitiveTypeInfo { type = attribute.type, required = attribute.required },
            };
        }

        private static JsonAttribute AttributeFromTypeInfo(TypeInfo typeInfo)
        {
            var id = "__neo_dialogue_action_value";
            switch (typeInfo.type)
            {
                case AttributeType.Null:
                    return new NullAttribute { id = id, _id = id, type = AttributeType.Null };
                case AttributeType.Bool:
                    return new BoolAttribute { id = id, _id = id, type = AttributeType.Bool };
                case AttributeType.Int:
                    return new IntAttribute { id = id, _id = id, type = AttributeType.Int };
                case AttributeType.Float:
                    return new FloatAttribute { id = id, _id = id, type = AttributeType.Float };
                case AttributeType.String:
                    return new StringAttribute { id = id, _id = id, type = AttributeType.String };
                case AttributeType.Custom:
                    return new CustomAttribute
                    {
                        id = id,
                        _id = id,
                        type = AttributeType.Custom,
                        customTypeId = ((CustomTypeInfo)typeInfo).typeId,
                    };
                case AttributeType.List:
                    return new ListAttribute
                    {
                        id = id,
                        _id = id,
                        type = AttributeType.List,
                        entryAttributeId = id,
                    };
                case AttributeType.Dictionary:
                    return new DictionaryAttribute
                    {
                        id = id,
                        _id = id,
                        type = AttributeType.Dictionary,
                        entryAttributeId = id,
                    };
                case AttributeType.Enum:
                    return new EnumAttribute
                    {
                        id = id,
                        _id = id,
                        type = AttributeType.Enum,
                        enumId = ((EnumTypeInfo)typeInfo).enumId,
                    };
                case AttributeType.Lookup:
                    return new LookupAttribute
                    {
                        id = id,
                        _id = id,
                        type = AttributeType.Lookup,
                        collectionAttributeId = id,
                    };
                default:
                    throw new NSGetterRuntimeError(
                        $"Unsupported write target type '{typeInfo.type}'.");
            }
        }

        private static TypeInfo EntryTypeInfo(TypeInfo typeInfo)
        {
            if (typeInfo is LookupTypeInfo lookupTypeInfo)
            {
                return lookupTypeInfo.entryTypeInfo;
            }
            if (typeInfo is CollectionTypeInfo collectionTypeInfo)
            {
                return collectionTypeInfo.entryTypeInfo;
            }
            throw new NSGetterRuntimeError("Collection target is missing entry type info.");
        }

        private static AttributeValue CreateValueRow(
            NeoClient client,
            JsonAttribute attribute,
            object? value,
            string id,
            string createdAt,
            string updatedAt)
        {
            var payload = value is INeoValuePayloadProvider provider
                ? provider.ToNeoValuePayload()
                : value;
            client.SetSavePayloadRows(payload);
            return AttributeValueFactory.Create(
                attribute,
                payload,
                id,
                createdAt,
                updatedAt);
        }

        private static object? ReadRowValue(AttributeValue row)
        {
            return row switch
            {
                BoolAttributeValue b => b.value,
                NumberAttributeValue n => n.value,
                StringAttributeValue s => s.value,
                ArrayAttributeValue a => a.value,
                ObjectAttributeValue o => o.value,
                NullAttributeValue => null,
                _ => null,
            };
        }

        private static double ToDouble(object? value, string name)
        {
            switch (value)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                default:
                    throw new NSGetterRuntimeError($"{name} must be numeric.");
            }
        }

        private static int ToInt(object? value, string name)
        {
            var numeric = ToDouble(value, name);
            if (numeric != Math.Truncate(numeric))
            {
                throw new NSGetterRuntimeError($"{name} must be an integer.");
            }
            return (int)numeric;
        }

        private static string ToStringKey(object? value, string name)
        {
            if (value is string s) return s;
            throw new NSGetterRuntimeError($"{name} must be a string.");
        }

        private static string ResolveLookupSelectionId(
            NeoClient client,
            LookupTypeInfo lookupTypeInfo,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            if (!client.TryGetAttribute(lookupTypeInfo.collectionAttributeId, out JsonAttribute? collectionAttribute))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection attribute '{lookupTypeInfo.collectionAttributeId}' was not found.");
            }
            string? collectionValueId = client.TryResolveLookupCollectionValueId(
                collectionAttribute.id,
                lookupTypeInfo.collectionValueId,
                out string? resolvedCollectionValueId)
                    ? resolvedCollectionValueId
                    : null;
            if (collectionValueId is null || !client.TryGetValue(collectionValueId, out AttributeValue? collectionValue))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection value '{collectionValueId ?? "<null>"}' was not found.");
            }

            if (lookupTypeInfo.entryTypeInfo.type == AttributeType.Custom)
            {
                string? valueId = value is string id
                    ? id
                    : FindValueId(value, ctx);
                if (string.IsNullOrWhiteSpace(valueId))
                {
                    throw new NSGetterRuntimeError(
                        "Lookup set custom argument must be a selected value id or generated custom value.");
                }
                if (!LookupCollectionContainsValueId(collectionValue, valueId!))
                {
                    throw new NSGetterRuntimeError(
                        $"Lookup selection id '{valueId}' is not present in the configured lookup collection.");
                }
                return valueId!;
            }

            string? matchedValueId = FindLookupCollectionValueByPayload(client, collectionValue, value);
            if (matchedValueId is null)
            {
                throw new NSGetterRuntimeError(
                    "Lookup set argument was not found in the configured lookup collection.");
            }
            return matchedValueId;
        }

        private static bool LookupCollectionContainsValueId(
            AttributeValue collectionValue,
            string valueId)
        {
            return collectionValue switch
            {
                ArrayAttributeValue array when array.value is not null =>
                    Array.IndexOf(array.value, valueId) >= 0,
                ObjectAttributeValue obj when obj.value is not null =>
                    obj.value.ContainsValue(valueId),
                _ => false,
            };
        }

        private static string? FindLookupCollectionValueByPayload(
            NeoClient client,
            AttributeValue collectionValue,
            object? value)
        {
            IEnumerable<string> childIds = collectionValue switch
            {
                ArrayAttributeValue array when array.value is not null => array.value,
                ObjectAttributeValue obj when obj.value is not null => obj.value.Values,
                _ => Array.Empty<string>(),
            };
            foreach (var childId in childIds)
            {
                if (!client.TryGetValue(childId, out AttributeValue? child)) continue;
                if (JsEqual(ReadRowValue(child), value)) return childId;
            }
            return null;
        }

        private static bool JsEqual(object? a, object? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is double da && b is double db) return da == db;
            if (a is double da2 && b is int ib) return da2 == ib;
            if (a is int ia && b is double db2) return ia == db2;
            return Equals(a, b);
        }

        private static void MutateLocalCollection(
            object? local,
            string mutation,
            object?[] args)
        {
            if (local is object?[] array)
            {
                var arrayList = new List<object?>(array);
                MutateLocalList(arrayList, mutation, args);
                return;
            }
            if (local is List<object?> list)
            {
                MutateLocalList(list, mutation, args);
                return;
            }
            if (local is IDictionary<string, object?> dict)
            {
                MutateLocalDictionary(dict, mutation, args);
                return;
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static void MutateLocalList(
            List<object?> list,
            string mutation,
            object?[] args)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Add:
                    list.Add(args[0]);
                    return;
                case CollectionMutationKind.Remove:
                    list.RemoveAll(item => JsEqual(item, args[0]));
                    return;
                case CollectionMutationKind.RemoveAt:
                    list.RemoveAt(ToInt(args[0], "RemoveAt index"));
                    return;
                case CollectionMutationKind.Clear:
                    list.Clear();
                    return;
                default:
                    throw new NSGetterRuntimeError($"Unsupported collection mutation '{mutation}'.");
            }
        }

        private static void MutateLocalDictionary(
            IDictionary<string, object?> dict,
            string mutation,
            object?[] args)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Add:
                    dict[ToStringKey(args[0], "Dictionary Add key")] = args[1];
                    return;
                case CollectionMutationKind.Remove:
                    dict.Remove(ToStringKey(args[0], "Dictionary Remove key"));
                    return;
                case CollectionMutationKind.Clear:
                    dict.Clear();
                    return;
                default:
                    throw new NSGetterRuntimeError($"Unsupported dictionary mutation '{mutation}'.");
            }
        }

        private abstract class NeoResolvedWriteTarget
        {
            public abstract object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx);

            public abstract void Write(NeoClient client, object? value);
        }

        private sealed class NeoRowWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string rowId;
            private readonly TypeInfo typeInfo;

            public NeoRowWriteTarget(string rowId, TypeInfo typeInfo)
            {
                this.rowId = rowId;
                this.typeInfo = typeInfo;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out AttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{rowId}'.");
                }
                return ReadRowValue(row);
            }

            public override void Write(NeoClient client, object? value)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out AttributeValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{rowId}'.");
                }
                var next = CreateValueRow(
                    client,
                    AttributeFromTypeInfo(typeInfo),
                    value,
                    rowId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.typeId = existing.typeId;
                client.SetSaveValue(next);
            }
        }

        private sealed class NeoCustomMemberWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly string key;
            private readonly JsonAttribute attribute;

            public NeoCustomMemberWriteTarget(
                string parentRowId,
                string key,
                JsonAttribute attribute)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.attribute = attribute;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentRowId, out ObjectAttributeValue? parent)
                    || parent.value == null
                    || !parent.value.TryGetValue(key, out string childId)
                    || !client.TryGetValue(childId, out AttributeValue? child))
                {
                    return null;
                }
                return ReadRowValue(child);
            }

            public override void Write(NeoClient client, object? value)
            {
                EnsureSaveRow(client, parentRowId);
                if (!client.TryGetValue(parentRowId, out ObjectAttributeValue? parent))
                {
                    throw new NSGetterRuntimeError($"Missing parent row '{parentRowId}'.");
                }
                parent.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
                if (parent.value.TryGetValue(key, out string existingId)
                    && client.TryGetValue(existingId, out AttributeValue? existing))
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            AttributeTypeInfo(attribute),
                            out string? referenceId))
                    {
                        parent.value[key] = referenceId!;
                        parent.updatedAt = now;
                        client.SetSaveValue(parent);
                        client.RemoveSaveValueAndDescendantsIfUnlinked(existingId);
                        return;
                    }
                    var next = CreateValueRow(client, attribute, value, existingId, existing.createdAt, now);
                    next.typeId = existing.typeId;
                    client.SetSaveValue(next);
                }
                else
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            AttributeTypeInfo(attribute),
                            out string? referenceId))
                    {
                        parent.value[key] = referenceId!;
                        parent.updatedAt = now;
                        client.SetSaveValue(parent);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(client, attribute, value, childId, now, now);
                    client.SetSaveValue(next);
                    parent.value[key] = childId;
                }
                parent.updatedAt = now;
                client.SetSaveValue(parent);
            }
        }

        private sealed class NeoDictionaryEntryWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly string key;
            private readonly TypeInfo typeInfo;

            public NeoDictionaryEntryWriteTarget(
                string parentRowId,
                string key,
                TypeInfo typeInfo)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.typeInfo = typeInfo;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentRowId, out ObjectAttributeValue? parent)
                    || parent.value == null
                    || !parent.value.TryGetValue(key, out string childId)
                    || !client.TryGetValue(childId, out AttributeValue? child))
                {
                    return null;
                }
                return ReadRowValue(child);
            }

            public override void Write(NeoClient client, object? value)
            {
                var target = new NeoDictionaryWriteTarget(parentRowId, typeInfo);
                target.Set(client, key, value);
            }
        }

        private sealed class NeoListIndexWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly int index;
            private readonly TypeInfo typeInfo;

            public NeoListIndexWriteTarget(string parentRowId, int index, TypeInfo typeInfo)
            {
                this.parentRowId = parentRowId;
                this.index = index;
                this.typeInfo = typeInfo;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentRowId, out ArrayAttributeValue? parent)
                    || parent.value == null
                    || index < 0
                    || index >= parent.value.Length
                    || !client.TryGetValue(parent.value[index], out AttributeValue? child))
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                return ReadRowValue(child);
            }

            public override void Write(NeoClient client, object? value)
            {
                EnsureSaveRow(client, parentRowId);
                if (!client.TryGetValue(parentRowId, out ArrayAttributeValue? parent)
                    || parent.value == null
                    || index < 0
                    || index >= parent.value.Length)
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                var childId = parent.value[index];
                if (TryGetCustomValueReferenceId(value, typeInfo, out string? referenceId))
                {
                    parent.value[index] = referenceId!;
                    parent.updatedAt = DateTime.UtcNow.ToString("o");
                    client.SetSaveValue(parent);
                    client.RemoveSaveValueAndDescendantsIfUnlinked(childId);
                    return;
                }
                if (!client.TryGetValue(childId, out AttributeValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing list child row '{childId}'.");
                }
                var next = CreateValueRow(
                    client,
                    AttributeFromTypeInfo(typeInfo),
                    value,
                    childId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.typeId = existing.typeId;
                client.SetSaveValue(next);
            }
        }

        private abstract class NeoResolvedCollectionTarget
        {
            public abstract void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx);
        }

        private sealed class NeoListWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;

            public NeoListWriteTarget(string rowId, TypeInfo entryTypeInfo)
            {
                this.rowId = rowId;
                this.entryTypeInfo = entryTypeInfo;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out ArrayAttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing list row '{rowId}'.");
                }
                row.value ??= Array.Empty<string>();
                var now = DateTime.UtcNow.ToString("o");
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                    {
                        if (TryGetCustomValueReferenceId(
                                args[0],
                                entryTypeInfo,
                                out string? referenceId))
                        {
                            var referencedNext = new string[row.value.Length + 1];
                            Array.Copy(row.value, referencedNext, row.value.Length);
                            referencedNext[row.value.Length] = referenceId!;
                            row.value = referencedNext;
                            row.updatedAt = now;
                            client.SetSaveValue(row);
                            return;
                        }
                        var childId = Guid.NewGuid().ToString();
                        var child = CreateValueRow(
                            client,
                            AttributeFromTypeInfo(entryTypeInfo),
                            args[0],
                            childId,
                            now,
                            now);
                        client.SetSaveValue(child);
                        var next = new string[row.value.Length + 1];
                        Array.Copy(row.value, next, row.value.Length);
                        next[row.value.Length] = childId;
                        row.value = next;
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        return;
                    }
                    case CollectionMutationKind.RemoveAt:
                        RemoveAt(client, row, ToInt(args[0], "RemoveAt index"), now);
                        return;
                    case CollectionMutationKind.Remove:
                    {
                        string? referenceId = TryGetCustomValueReferenceId(
                            args[0],
                            entryTypeInfo,
                            out string? matchedReferenceId)
                                ? matchedReferenceId
                                : null;
                        for (int i = 0; i < row.value.Length; i++)
                        {
                            if (referenceId != null && row.value[i] == referenceId)
                            {
                                RemoveAt(client, row, i, now);
                                return;
                            }
                            if (!client.TryGetValue(row.value[i], out AttributeValue? child)) continue;
                            if (!JsEqual(ReadRowValue(child), args[0])) continue;
                            RemoveAt(client, row, i, now);
                            return;
                        }
                        return;
                    }
                    case CollectionMutationKind.Clear:
                    {
                        var removedIds = row.value;
                        row.value = Array.Empty<string>();
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        foreach (var childId in removedIds)
                        {
                            client.RemoveSaveValueAndDescendantsIfUnlinked(childId);
                        }
                        return;
                    }
                    default:
                        throw new NSGetterRuntimeError($"Unsupported list mutation '{mutation}'.");
                }
            }

            private static void RemoveAt(
                NeoClient client,
                ArrayAttributeValue row,
                int index,
                string now)
            {
                if (row.value == null || index < 0 || index >= row.value.Length)
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                string removedId = row.value[index];
                var next = new string[row.value.Length - 1];
                for (int i = 0, j = 0; i < row.value.Length; i++)
                {
                    if (i == index) continue;
                    next[j++] = row.value[i];
                }
                row.value = next;
                row.updatedAt = now;
                client.SetSaveValue(row);
                client.RemoveSaveValueAndDescendantsIfUnlinked(removedId);
            }
        }

        private sealed class NeoLookupSetWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly LookupTypeInfo typeInfo;

            public NeoLookupSetWriteTarget(string rowId, LookupTypeInfo typeInfo)
            {
                this.rowId = rowId;
                this.typeInfo = typeInfo;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out ArrayAttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing lookup row '{rowId}'.");
                }
                row.value ??= Array.Empty<string>();
                var now = DateTime.UtcNow.ToString("o");
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                    {
                        string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                        if (Array.IndexOf(row.value, selectionId) >= 0) return;
                        var next = new string[row.value.Length + 1];
                        Array.Copy(row.value, next, row.value.Length);
                        next[row.value.Length] = selectionId;
                        row.value = next;
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        return;
                    }
                    case CollectionMutationKind.Remove:
                    {
                        string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                        int index = Array.IndexOf(row.value, selectionId);
                        if (index < 0) return;
                        var next = new string[row.value.Length - 1];
                        for (int i = 0, j = 0; i < row.value.Length; i++)
                        {
                            if (i == index) continue;
                            next[j++] = row.value[i];
                        }
                        row.value = next;
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        return;
                    }
                    case CollectionMutationKind.Clear:
                        row.value = Array.Empty<string>();
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        return;
                    default:
                        throw new NSGetterRuntimeError($"Unsupported lookup set mutation '{mutation}'.");
                }
            }
        }

        private sealed class NeoDictionaryWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;

            public NeoDictionaryWriteTarget(string rowId, TypeInfo entryTypeInfo)
            {
                this.rowId = rowId;
                this.entryTypeInfo = entryTypeInfo;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                        Set(client, ToStringKey(args[0], "Dictionary Add key"), args[1]);
                        return;
                    case CollectionMutationKind.Remove:
                        Remove(client, ToStringKey(args[0], "Dictionary Remove key"));
                        return;
                    case CollectionMutationKind.Clear:
                        Clear(client);
                        return;
                    default:
                        throw new NSGetterRuntimeError($"Unsupported dictionary mutation '{mutation}'.");
                }
            }

            public void Set(NeoClient client, string key, object? value)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing dictionary row '{rowId}'.");
                }
                row.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
                if (row.value.TryGetValue(key, out string existingId)
                    && client.TryGetValue(existingId, out AttributeValue? existing))
                {
                    if (TryGetCustomValueReferenceId(value, entryTypeInfo, out string? referenceId))
                    {
                        row.value[key] = referenceId!;
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        client.RemoveSaveValueAndDescendantsIfUnlinked(existingId);
                        return;
                    }
                    var next = CreateValueRow(
                        client,
                        AttributeFromTypeInfo(entryTypeInfo),
                        value,
                        existingId,
                        existing.createdAt,
                        now);
                    next.typeId = existing.typeId;
                    client.SetSaveValue(next);
                }
                else
                {
                    if (TryGetCustomValueReferenceId(value, entryTypeInfo, out string? referenceId))
                    {
                        row.value[key] = referenceId!;
                        row.updatedAt = now;
                        client.SetSaveValue(row);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(
                        client,
                        AttributeFromTypeInfo(entryTypeInfo),
                        value,
                        childId,
                        now,
                        now);
                    client.SetSaveValue(next);
                    row.value[key] = childId;
                }
                row.updatedAt = now;
                client.SetSaveValue(row);
            }

            private void Remove(NeoClient client, string key)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row)
                    || row.value == null
                    || !row.value.TryGetValue(key, out string removedId))
                {
                    return;
                }
                row.value.Remove(key);
                row.updatedAt = DateTime.UtcNow.ToString("o");
                client.SetSaveValue(row);
                client.RemoveSaveValueAndDescendantsIfUnlinked(removedId);
            }

            private void Clear(NeoClient client)
            {
                EnsureSaveRow(client, rowId);
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row)
                    || row.value == null)
                {
                    return;
                }
                var removedIds = new List<string>(row.value.Values);
                row.value.Clear();
                row.updatedAt = DateTime.UtcNow.ToString("o");
                client.SetSaveValue(row);
                foreach (var childId in removedIds)
                {
                    client.RemoveSaveValueAndDescendantsIfUnlinked(childId);
                }
            }
        }
    }
}
