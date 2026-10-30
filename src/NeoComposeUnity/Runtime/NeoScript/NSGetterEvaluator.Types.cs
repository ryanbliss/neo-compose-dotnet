// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>P127 §2. The members of a <c>Type</c> value, with .NET 10's facts in NeoScript spelling.</summary>
    public static partial class NSGetterEvaluator
    {
        private static readonly TypeBinding ObjectBinding = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
        private static readonly TypeBinding ValueTypeBinding =
            TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.ValueType));
        private static readonly TypeBinding EnumBinding =
            TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.Enum));

        private sealed class InterfaceClosures
        {
            internal readonly Dictionary<TypeBinding, List<TypeBinding>> Ordered = new();
            internal readonly Dictionary<TypeBinding, HashSet<TypeBinding>> Membership = new();
            internal int Visits;
        }

        private static readonly ConditionalWeakTable<object, InterfaceClosures> interfaceClosures = new();

        // Counts traversed edges, including duplicate edges in diamonds.
        internal static int InterfaceClosureVisits(NeoClient client) => interfaceClosures.GetOrCreateValue(client.SchemaResolution).Visits;

        private static List<TypeBinding> ParameterConstraints(TypeBinding type, Context ctx)
        {
            TypeDefinition owner = type.Definition!;
            if (owner.BuiltInArity is not null)
            {
                int position = ParameterPosition(type, ctx);
                NeoTypeNames.BuiltInParameterName(owner, position);
                // Nullable<T> and ValueTuple's TRest are the built-in struct constraints.
                return HasBuiltInValueConstraint(type)
                    ? new List<TypeBinding> { ValueTypeBinding }
                    : new List<TypeBinding>();
            }
            List<GenericParamDeclaration>? parameters = owner.Type switch
            {
                MemberKind.Class => ClassOf(owner.ClassId!, ctx).genericParams,
                MemberKind.Interface => InterfaceOf(owner.InterfaceId!, ctx).genericParams,
                _ => null,
            };
            var result = new List<TypeBinding>();
            if (parameters is null)
                throw new NSGetterRuntimeError($"Reflection on constraints of '{TypeName(type, ctx)}' isn't supported yet (P127 B3).");
            GenericParamDeclaration parameter = parameters.Find(item => item.id == type.Param)
                ?? throw new NSGetterRuntimeError($"Type parameter '{type.Param}' was not found.");
            TypeBinding construction = OpenConstruction(owner, ctx);
            foreach (GenericParamConstraint constraint in parameter.constraints ?? new List<GenericParamConstraint>())
            {
                TypeBinding binding = constraint.kind switch
                {
                    NeoGenericParamConstraintKind.Class => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: constraint.classId), constraint.classArguments),
                    NeoGenericParamConstraintKind.Interface => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: constraint.interfaceId), constraint.interfaceArguments),
                    _ => throw new NSGetterRuntimeError($"Reflection on the {constraint.kind} constraint of '{parameter.name}' isn't supported yet (P127 B3)."),
                };
                result.Add(Construct(binding, construction));
            }
            return result;
        }

        private static bool HasBuiltInValueConstraint(TypeBinding type) =>
            type.Definition is { Nullable: true }
            || type.Definition is { Type: MemberKind.Tuple, Arity: 8 } && type.Param == "7";

        /// <summary>A type's reflection facts (§2's per-kind table).</summary>
        [Flags]
        private enum TypeFacts
        {
            None = 0,
            Class = 1,
            Interface = 2,
            Enum = 4,
            ValueType = 8,
            Primitive = 16,
            Abstract = 32,
            Sealed = 64,
        }

        private static object? EvalTypeMember(TypeMemberPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? receiver = EvalPointer(pointer.receiver, scope, ctx);
            if (receiver is null && pointer.optional == true)
                return null;
            // Supported Type methods have at most one argument; properties have none.
            object? argument = pointer.args.Length == 0 ? null : EvalPointer(pointer.args[0], scope, ctx);
            if (receiver is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            TypeBinding type = ((NeoTypeValue)receiver).Binding;
            switch (pointer.member)
            {
                case TypeMemberKind.Name:
                    return type.Head is { IsOpaque: true } opaque
                        ? opaque.OpaqueName!.Substring(opaque.OpaqueName.LastIndexOf('.') + 1)
                        : TypeName(type, ctx);
                case TypeMemberKind.FullName:
                    return TypeName(type, ctx);
                case TypeMemberKind.Namespace:
                    return type.Head is { IsOpaque: true } named
                        ? named.OpaqueName!.Substring(0, named.OpaqueName.LastIndexOf('.'))
                        : null;
                case TypeMemberKind.BaseType:
                    return BaseTypeOf(type, ctx) is { } baseType ? NeoTypeValue.Of(baseType) : null;
                case TypeMemberKind.GetInterfaces:
                    return TypeValues(InterfacesOf(type, ctx));
                case TypeMemberKind.IsAssignableFrom:
                    return Box(argument is NeoTypeValue from && IsAssignableFrom(type, from.Binding, ctx));
                case TypeMemberKind.IsInstanceOfType:
                    {
                        object? value = argument;
                        if (value is null || ContainsParameters(type))
                            return Box(false);
                        if (value is NeoBox box && type.Head is { Type: MemberKind.Interface })
                            return Box(BoxMatches(box, NeoTypeBindings.ToTypeInfo(type), ctx));
                        return Box(IsAssignableFrom(type, RuntimeTypeOfValue(value, null, ctx), ctx));
                    }
                case TypeMemberKind.IsSubclassOf:
                    {
                        if (argument is not NeoTypeValue other)
                            throw new NeoRuntimeError(NeoErrorClass.NeoArgumentNullError, "Value cannot be null. (Parameter 'c')");
                        return Box(!type.Equals(other.Binding) && BaseChainContains(type, other.Binding, ctx));
                    }
                case TypeMemberKind.IsClass:
                    return Box((FactsOf(type, ctx) & TypeFacts.Class) != 0);
                case TypeMemberKind.IsInterface:
                    return Box((FactsOf(type, ctx) & TypeFacts.Interface) != 0);
                case TypeMemberKind.IsEnum:
                    return Box((FactsOf(type, ctx) & TypeFacts.Enum) != 0);
                case TypeMemberKind.IsValueType:
                    return Box((FactsOf(type, ctx) & TypeFacts.ValueType) != 0);
                case TypeMemberKind.IsPrimitive:
                    return Box((FactsOf(type, ctx) & TypeFacts.Primitive) != 0);
                case TypeMemberKind.IsAbstract:
                    return Box((FactsOf(type, ctx) & TypeFacts.Abstract) != 0);
                case TypeMemberKind.IsSealed:
                    return Box((FactsOf(type, ctx) & TypeFacts.Sealed) != 0);
                case TypeMemberKind.IsGenericType:
                    return Box(type.Kind == TypeBindingKind.Definition || GenericDefinitionOf(type) is not null);
                case TypeMemberKind.IsGenericTypeDefinition:
                    return Box(type.Kind == TypeBindingKind.Definition);
                case TypeMemberKind.GenericTypeArguments:
                    return TypeValues(GenericArgumentsOf(type, ctx));
                case TypeMemberKind.GetGenericTypeDefinition:
                    {
                        if (type.Kind == TypeBindingKind.Definition)
                            return receiver;
                        TypeDefinition definition = GenericDefinitionOf(type)
                            ?? throw new NeoRuntimeError(NeoErrorClass.NeoInvalidOperationError, "This operation is only valid on generic types.");
                        return NeoTypeValue.Of(TypeBinding.OfDefinition(definition));
                    }
                case TypeMemberKind.IsGenericParameter:
                    return Box(type.Kind == TypeBindingKind.Open);
                case TypeMemberKind.GenericParameterPosition:
                    {
                        if (type.Kind != TypeBindingKind.Open)
                        {
                            throw new NeoRuntimeError(
                                NeoErrorClass.NeoInvalidOperationError,
                                "Method may only be called on a Type for which Type.IsGenericParameter is true.");
                        }
                        return Box((double)ParameterPosition(type, ctx));
                    }
                case TypeMemberKind.ContainsGenericParameters:
                    return Box(ContainsParameters(type));
                default:
                    throw new NSGetterRuntimeError($"Unknown Type member '{pointer.member}'.");
            }
        }

        private static object?[] TypeValues(List<TypeBinding> types)
        {
            var values = new object?[types.Count];
            for (int index = 0; index < values.Length; index++)
                values[index] = NeoTypeValue.Of(types[index]);
            return values;
        }

        private static TypeBinding Opaque(string fullName) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Unknown, true, opaqueName: fullName));

        private static bool IsNullableValue(TypeBinding type) =>
            type.Kind == TypeBindingKind.Closed && !type.Head!.Required && TypeBindings.IsValueType(type.Head);

        private static TypeFacts FactsOf(TypeBinding type, Context ctx)
        {
            if (type.Kind == TypeBindingKind.Open)
                return HasBuiltInValueConstraint(type) ? TypeFacts.ValueType : TypeFacts.Class;
            if (type.Kind == TypeBindingKind.Definition)
                return DefinitionFacts(type.Definition!, ctx);
            TypeHead head = type.Head!;
            if (head.IsOpaque)
            {
                return head.OpaqueName == "UnityEngine.Object"
                    ? TypeFacts.Class
                    : TypeFacts.Class | TypeFacts.Abstract;
            }
            if (!head.Required && TypeBindings.IsValueType(head))
                return TypeFacts.ValueType | TypeFacts.Sealed;
            switch (head.Type)
            {
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.Bool:
                    return TypeFacts.ValueType | TypeFacts.Primitive | TypeFacts.Sealed;
                case MemberKind.Enum:
                    return TypeFacts.ValueType | TypeFacts.Enum | TypeFacts.Sealed;
                case MemberKind.Class:
                    return ClassFacts(head.ClassId!, ctx);
                case MemberKind.Interface:
                    return TypeFacts.Interface | TypeFacts.Abstract;
                case MemberKind.Object:
                    return head.ObjectType switch
                    {
                        null or NeoObjectTypeKind.CultureInfo => TypeFacts.Class,
                        NeoObjectTypeKind.NumberFormatInfo => TypeFacts.Class | TypeFacts.Sealed,
                        _ => TypeFacts.Class | TypeFacts.Abstract,
                    };
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                    return TypeFacts.Class;
                default:
                    // decimal, vectors, Color and tuples are sealed structs; string, assets,
                    // references, dialogue references and delegates are sealed classes.
                    return TypeBindings.IsValueType(head)
                        ? TypeFacts.ValueType | TypeFacts.Sealed
                        : TypeFacts.Class | TypeFacts.Sealed;
            }
        }

        /// <summary>A definition reports the facts of its open type.</summary>
        private static TypeFacts DefinitionFacts(TypeDefinition definition, Context ctx)
        {
            if (definition.Nullable)
                return TypeFacts.ValueType | TypeFacts.Sealed;
            return definition.Type switch
            {
                MemberKind.Class => ClassFacts(definition.ClassId!, ctx),
                MemberKind.Interface => TypeFacts.Interface | TypeFacts.Abstract,
                MemberKind.Tuple => TypeFacts.ValueType | TypeFacts.Sealed,
                MemberKind.List or MemberKind.Set or MemberKind.Dictionary => TypeFacts.Class,
                _ => TypeFacts.Class | TypeFacts.Sealed,
            };
        }

        private static TypeFacts ClassFacts(string classId, Context ctx)
        {
            NeoSchemaClass schemaClass = ClassOf(classId, ctx);
            TypeFacts facts = TypeFacts.Class;
            if (schemaClass.IsAbstract)
                facts |= TypeFacts.Abstract;
            if (schemaClass.Modifier == NeoClassModifierKind.Sealed)
                facts |= TypeFacts.Sealed;
            return facts;
        }

        private static NeoSchemaClass ClassOf(string classId, Context ctx) =>
            ctx.client.TryGetClass(classId, out NeoSchemaClass? schemaClass)
                ? schemaClass
                : throw new NSGetterRuntimeError($"Class '{classId}' was not found.");

        private static Interface InterfaceOf(string interfaceId, Context ctx) =>
            ctx.client.ProjectDataForRuntime.interfaces is { } interfaces && interfaces.TryGetValue(interfaceId, out Interface? record)
                ? record
                : throw new NSGetterRuntimeError($"Interface '{interfaceId}' was not found.");

        /// <summary>A definition read as its open construction: <c>Box&lt;&gt;</c> as <c>Box&lt;T&gt;</c> over its own parameters.</summary>
        private static TypeBinding OpenConstruction(TypeDefinition definition, Context ctx)
        {
            List<GenericParamDeclaration>? parameters = definition.Type switch
            {
                MemberKind.Class => ClassOf(definition.ClassId!, ctx).genericParams,
                MemberKind.Interface => InterfaceOf(definition.InterfaceId!, ctx).genericParams,
                _ => null,
            };
            var bindings = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            foreach (GenericParamDeclaration parameter in parameters ?? new List<GenericParamDeclaration>())
                bindings[parameter.id] = TypeBinding.Open(definition, parameter.id);
            TypeHead head = definition.Type == MemberKind.Class
                ? new TypeHead(MemberKind.Class, true, classId: definition.ClassId)
                : new TypeHead(MemberKind.Interface, true, interfaceId: definition.InterfaceId);
            return TypeBinding.Closed(head, bindings);
        }

        /// <summary>§2 <c>BaseType</c>; null for <c>object</c>, interfaces and their definitions.</summary>
        private static TypeBinding? BaseTypeOf(TypeBinding type, Context ctx)
        {
            if (type.Kind == TypeBindingKind.Open)
                return ParameterConstraints(type, ctx).Find(constraint => constraint.Head!.Type != MemberKind.Interface) ?? ObjectBinding;
            if (type.Kind == TypeBindingKind.Definition)
            {
                TypeDefinition definition = type.Definition!;
                if (definition.Nullable || definition.Type == MemberKind.Tuple)
                    return ValueTypeBinding;
                return definition.Type switch
                {
                    MemberKind.Class or MemberKind.Interface => BaseTypeOf(OpenConstruction(definition, ctx), ctx),
                    MemberKind.NSDelegate => Opaque("System.MulticastDelegate"),
                    MemberKind.NSAction => Opaque("NeoCompose.Runtime.NeoActionBase"),
                    _ => ObjectBinding,
                };
            }
            TypeHead head = type.Head!;
            if (head.IsOpaque)
            {
                return head.OpaqueName == "System.MulticastDelegate"
                    ? Opaque("System.Delegate")
                    : ObjectBinding;
            }
            if (head.Type == MemberKind.Enum && head.Required)
                return EnumBinding;
            if (TypeBindings.IsValueType(head))
                return ValueTypeBinding;
            switch (head.Type)
            {
                case MemberKind.Class:
                    return ClassBaseType(type, ctx);
                case MemberKind.Interface:
                    return null;
                case MemberKind.Object:
                    return head.ObjectType switch
                    {
                        null => null,
                        NeoObjectTypeKind.Enum => ValueTypeBinding,
                        NeoObjectTypeKind.Type => Opaque("System.Reflection.MemberInfo"),
                        _ => ObjectBinding,
                    };
                case MemberKind.NSDelegate:
                    return Opaque("System.MulticastDelegate");
                case MemberKind.NSAction:
                    return Opaque("NeoCompose.Runtime.NeoActionBase");
                case MemberKind.Sprite:
                case MemberKind.Audio:
                    return Opaque("UnityEngine.Object");
                default:
                    return ObjectBinding;
            }
        }

        /// <summary>A class construction's base: its base class over the construction's arguments, else <c>object</c>.</summary>
        private static TypeBinding ClassBaseType(TypeBinding type, Context ctx)
        {
            string classId = type.Head!.ClassId!;
            NeoSchemaClass schemaClass = ClassOf(classId, ctx);
            if (schemaClass.extendsClassId is not { } baseId)
                return ObjectBinding;
            NeoSchemaClass baseClass = ClassOf(baseId, ctx);
            if (baseClass.genericParams is not { Count: > 0 } parameters)
                return TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: baseId));
            var bindings = new Dictionary<string, TypeBinding>(parameters.Count, StringComparer.Ordinal);
            foreach (GenericParamDeclaration parameter in parameters)
                bindings[parameter.id] = Construct(schemaClass.extendsGenericBindings![parameter.id], type);
            return TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: baseId), bindings);
        }

        /// <summary><paramref name="argument"/> with the construction's own parameters replaced by its arguments.</summary>
        private static TypeBinding Construct(TypeBinding argument, TypeBinding construction) =>
            NeoTypeSources.Substitute(argument, open =>
                construction.Bindings is { } bindings
                && OwnsParameter(construction.Head!, open.Definition!)
                && bindings.TryGetValue(open.Param!, out TypeBinding? bound)
                    ? bound
                    : null);

        private static bool OwnsParameter(TypeHead head, TypeDefinition owner) =>
            head.Type == MemberKind.Class
                ? owner.Type == MemberKind.Class && owner.ClassId == head.ClassId
                : owner.Type == MemberKind.Interface && owner.InterfaceId == head.InterfaceId;

        /// <summary>
        /// §2 <c>GetInterfaces()</c> of a class or interface: every interface
        /// it implements through its chain and their bases, closed over its
        /// arguments. Built-in types use §8.1's canonical conformance table.
        /// </summary>
        private static List<TypeBinding> InterfacesOf(TypeBinding type, Context ctx)
        {
            InterfaceClosures cache = interfaceClosures.GetOrCreateValue(ctx.client.SchemaResolution);
            if (cache.Ordered.TryGetValue(type, out List<TypeBinding> cached))
                return cached;
            var found = new List<TypeBinding>();
            var visited = new HashSet<TypeBinding>();
            Collect(type);
            cache.Ordered.Add(type, found);
            cache.Membership.Add(type, visited);
            return found;

            void Collect(TypeBinding current)
            {
                if (current.Kind == TypeBindingKind.Open)
                {
                    foreach (TypeBinding constraint in ParameterConstraints(current, ctx))
                    {
                        if (constraint.Head!.Type == MemberKind.Interface)
                            Add(constraint);
                        else if (!constraint.Equals(ValueTypeBinding))
                            Collect(constraint);
                    }
                    return;
                }
                if (IsNullableValue(current) || current.Head is { IsOpaque: true }
                    || current.Definition is { Nullable: true })
                {
                    return;
                }
                MemberKind kind = current.Head?.Type ?? current.Definition!.Type;
                if (kind is MemberKind.List or MemberKind.Set or MemberKind.Dictionary)
                {
                    int index = kind == MemberKind.Dictionary ? 1 : 0;
                    TypeBinding element = current.Kind == TypeBindingKind.Definition
                        ? TypeBinding.Open(current.Definition!, index.ToString(CultureInfo.InvariantCulture))
                        : current.At(index);
                    Add(TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: NeoSequenceContract.Enumerable),
                        new Dictionary<string, TypeBinding> { [NeoSequenceContract.EnumerableElement] = element }));
                    return;
                }
                if (current.Kind == TypeBindingKind.Definition)
                {
                    if (current.Definition!.Type == MemberKind.Tuple)
                    {
                        TypeDefinition definition = current.Definition;
                        var arguments = new Dictionary<string, TypeBinding>();
                        for (int i = 0; i < definition.Arity; i++)
                        {
                            string index = i.ToString(CultureInfo.InvariantCulture);
                            arguments[index] = TypeBinding.Open(definition, index);
                        }
                        current = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), arguments);
                    }
                    else if (current.Definition.Type is MemberKind.Class or MemberKind.Interface)
                        current = OpenConstruction(current.Definition, ctx);
                    else
                        return;
                }
                TypeHead head = current.Head!;
                if (head.Type == MemberKind.Object && head.ObjectType is null or NeoObjectTypeKind.ValueType)
                    return;
                if (NeoPrimitiveConformance.Kind(current) is not null)
                {
                    foreach (TypeBinding construction in NeoInterfaceDispatch.Of(ctx.client).PrimitiveClosure(current))
                        if (visited.Add(construction))
                            found.Add(construction);
                    return;
                }
                if (head.Type == MemberKind.Interface)
                {
                    AddBases(current);
                    return;
                }
                if (head.Type != MemberKind.Class)
                    return;
                for (TypeBinding? ancestor = current; ancestor is { Head: { Type: MemberKind.Class } }; ancestor = BaseTypeOf(ancestor, ctx))
                {
                    foreach (InterfaceConstruction construction in ClassOf(ancestor.Head!.ClassId!, ctx).implementsInterfaces ?? new List<InterfaceConstruction>())
                        Add(InterfaceBinding(construction, ancestor));
                }
            }

            void Add(TypeBinding item)
            {
                cache.Visits++;
                if (!visited.Add(item))
                    return;
                found.Add(item);
                AddBases(item);
            }

            void AddBases(TypeBinding item)
            {
                foreach (InterfaceConstruction construction in InterfaceOf(item.Head!.InterfaceId!, ctx).extendsInterfaces ?? new List<InterfaceConstruction>())
                    Add(InterfaceBinding(construction, item));
            }
        }

        /// <summary>An interface construction declared by <paramref name="owner"/>, closed over the owner's arguments.</summary>
        private static TypeBinding InterfaceBinding(InterfaceConstruction construction, TypeBinding owner)
        {
            var head = new TypeHead(MemberKind.Interface, true, interfaceId: construction.interfaceId);
            if (construction.arguments is not { Count: > 0 } arguments)
                return TypeBinding.Closed(head);
            var bindings = new Dictionary<string, TypeBinding>(arguments.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeBinding> pair in arguments)
                bindings[pair.Key] = Construct(pair.Value, owner);
            return TypeBinding.Closed(head, bindings);
        }

        private static bool BaseChainContains(TypeBinding type, TypeBinding ancestor, Context ctx)
        {
            // CLR interfaces report object as a superclass even though BaseType is null.
            if ((type.Head?.Type ?? type.Definition?.Type) == MemberKind.Interface
                && type.Kind != TypeBindingKind.Open && ancestor.Equals(ObjectBinding))
            {
                return true;
            }
            for (TypeBinding? current = BaseTypeOf(type, ctx); current is not null; current = BaseTypeOf(current, ctx))
            {
                if (current.Equals(ancestor))
                    return true;
            }
            return false;
        }

        /// <summary>§2 <c>IsAssignableFrom</c>: identity, <c>T?</c> from <c>T</c>, the base chain, or an implemented interface.</summary>
        private static bool IsAssignableFrom(TypeBinding type, TypeBinding from, Context ctx)
        {
            if (type.Equals(from))
                return true;
            if (IsNullableValue(type))
                return type.WithRequired(true).Equals(from);
            if (type.Kind == TypeBindingKind.Closed && type.Head!.Type == MemberKind.Object && type.Head.ObjectType is null)
                return true;
            if (type.Kind == TypeBindingKind.Closed && from.Kind == TypeBindingKind.Closed
                && from.Head!.Type is MemberKind.Class or MemberKind.Interface or MemberKind.NSDelegate
                && type.Head!.Type is MemberKind.Interface or MemberKind.NSDelegate)
                return NeoInterfaceDispatch.Of(ctx.client).Converts(from, type);
            if (type.Kind == TypeBindingKind.Closed && type.Head!.Type == MemberKind.Interface)
            {
                InterfacesOf(from, ctx);
                return interfaceClosures.GetOrCreateValue(ctx.client.SchemaResolution).Membership[from].Contains(type);
            }
            return BaseChainContains(from, type, ctx);
        }

        /// <summary>The definition a generic construction instantiates; null for a type that isn't one.</summary>
        private static TypeDefinition? GenericDefinitionOf(TypeBinding type)
        {
            if (type.Kind != TypeBindingKind.Closed)
                return null;
            TypeHead head = type.Head!;
            if (IsNullableValue(type))
                return new TypeDefinition(MemberKind.Unknown, nullable: true);
            int count = type.Bindings?.Count ?? 0;
            if (count == 0)
                return null;
            return head.Type switch
            {
                MemberKind.Class => new TypeDefinition(MemberKind.Class, classId: head.ClassId),
                MemberKind.Interface => new TypeDefinition(MemberKind.Interface, interfaceId: head.InterfaceId),
                MemberKind.Tuple => new TypeDefinition(MemberKind.Tuple, arity: Math.Min(count, 8)),
                MemberKind.NSDelegate or MemberKind.NSAction => new TypeDefinition(head.Type, arity: count),
                _ => new TypeDefinition(head.Type),
            };
        }

        /// <summary>
        /// §2 <c>GenericTypeArguments</c>: a declared head's arguments in
        /// parameter order, a built-in's by position, a nullable value type's
        /// underlying type, and a long tuple's first seven elements and its
        /// <c>TRest</c>. Empty for definitions and parameters.
        /// </summary>
        private static List<TypeBinding> GenericArgumentsOf(TypeBinding type, Context ctx)
        {
            var arguments = new List<TypeBinding>();
            if (type.Kind != TypeBindingKind.Closed)
                return arguments;
            if (IsNullableValue(type))
            {
                arguments.Add(type.WithRequired(true));
                return arguments;
            }
            if (type.Bindings is not { Count: > 0 } bindings)
                return arguments;
            TypeHead head = type.Head!;
            List<GenericParamDeclaration>? parameters = head.Type switch
            {
                MemberKind.Class => ClassOf(head.ClassId!, ctx).genericParams,
                MemberKind.Interface => InterfaceOf(head.InterfaceId!, ctx).genericParams,
                _ => null,
            };
            if (parameters is not null)
            {
                foreach (GenericParamDeclaration parameter in parameters)
                    arguments.Add(bindings[parameter.id]);
                return arguments;
            }
            if (head.Type == MemberKind.Tuple && bindings.Count > 7)
            {
                for (int index = 0; index < 7; index++)
                    arguments.Add(type.At(index));
                var rest = new Dictionary<string, TypeBinding>(bindings.Count - 7, StringComparer.Ordinal);
                for (int index = 7; index < bindings.Count; index++)
                    rest[(index - 7).ToString(CultureInfo.InvariantCulture)] = type.At(index);
                arguments.Add(TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), rest));
                return arguments;
            }
            for (int index = 0; index < bindings.Count; index++)
                arguments.Add(type.At(index));
            return arguments;
        }

        private static int ParameterPosition(TypeBinding parameter, Context ctx)
        {
            TypeDefinition owner = parameter.Definition!;
            List<GenericParamDeclaration>? parameters = owner.Type switch
            {
                MemberKind.Class => ClassOf(owner.ClassId!, ctx).genericParams,
                MemberKind.Interface => InterfaceOf(owner.InterfaceId!, ctx).genericParams,
                _ => null,
            };
            if (parameters is null)
                return int.Parse(parameter.Param!, CultureInfo.InvariantCulture);
            return parameters.FindIndex(declaration => declaration.id == parameter.Param);
        }

        private static bool ContainsParameters(TypeBinding type)
        {
            if (type.Kind != TypeBindingKind.Closed)
                return true;
            foreach (TypeBinding argument in type.Bindings?.Values ?? Array.Empty<TypeBinding>())
            {
                if (ContainsParameters(argument))
                    return true;
            }
            return false;
        }
    }
}
