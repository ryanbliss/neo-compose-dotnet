// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// P127. The schema answers primitive interface dispatch needs, built
    /// lazily and dropped with the client's other schema caches.
    /// </summary>
    internal sealed class NeoPrimitiveInterfaceSchema
    {
        private readonly NeoClient client;
        private Dictionary<string, string>? interfaceMemberNames;
        private readonly Dictionary<(string classId, string memberId), JsonMember> staticImplementations = new();
        private readonly Dictionary<string, NeoEquatableClass?> equatableClasses = new(StringComparer.Ordinal);
        private bool? anyEquatable;

        internal NeoPrimitiveInterfaceSchema(NeoClient client) => this.client = client;

        internal string ClassName(string classId) =>
            client.classes.TryGetValue(classId, out NeoSchemaClass? schemaClass) ? schemaClass.name : classId;

        /// <summary>P127 §2. An interface member's name, by member id.</summary>
        internal string InterfaceMemberName(string memberId)
        {
            if (interfaceMemberNames is null)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Interface contract in client.interfaces.Values)
                {
                    foreach (var member in contract.members)
                    {
                        // Project interface symbols are named `<interface>:member:<name>`.
                        names[member.Value.id] = member.Key;
                        names[$"{contract.id}:member:{member.Key}"] = member.Key;
                    }
                }
                interfaceMemberNames = names;
            }
            if (!interfaceMemberNames.TryGetValue(memberId, out string? name))
                throw new NSGetterRuntimeError($"Static interface member '{memberId}' is not declared by any interface.");
            return name;
        }

        /// <summary>P127 §2. The public static of <paramref name="classId"/> implementing an interface member.</summary>
        internal JsonMember StaticImplementation(string classId, string interfaceMemberId, Func<string> typeParameterName)
        {
            if (staticImplementations.TryGetValue((classId, interfaceMemberId), out JsonMember? cached))
                return cached;
            string name = InterfaceMemberName(interfaceMemberId);
            string className = ClassName(classId);
            string? memberId = null;
            foreach (MergedSchemaEntry entry in NeoSchemaClassInheritance.MergeSchemas(client.ResolveClassNode(classId).Chain))
            {
                if (entry.schemaKey == name)
                    memberId = entry.memberId;
            }
            if (memberId is null)
            {
                throw new NSGetterRuntimeError(
                    $"Type parameter '{typeParameterName()}' is bound to '{className}', which has no static member '{NeoPrimitiveInterfaces.DisplayName(name)}'.");
            }
            if (!client.TryGetMember(memberId, out JsonMember? member))
            {
                throw new NSGetterRuntimeError(
                    $"'{className}.{NeoPrimitiveInterfaces.DisplayName(name)}' references missing member '{memberId}'.");
            }
            if (member.Modifier != NeoMemberModifierKind.Static)
            {
                throw new NSGetterRuntimeError(
                    $"'{className}.{NeoPrimitiveInterfaces.DisplayName(name)}' is an instance member, so it can't implement a static interface member.");
            }
            staticImplementations[(classId, interfaceMemberId)] = member;
            return member;
        }

        /// <summary>P127 §7. Whether any class implements IEquatable.</summary>
        internal bool AnyEquatable
        {
            get
            {
                if (anyEquatable is { } known)
                    return known;
                bool any = false;
                foreach (string classId in client.classes.Keys)
                {
                    if (client.InterfaceImplementers.Implements(classId, NeoPrimitiveInterfaces.IEquatableId))
                    {
                        any = true;
                        break;
                    }
                }
                anyEquatable = any;
                return any;
            }
        }

        /// <summary>P127 §7. <paramref name="classId"/>'s IEquatable implementation, or null.</summary>
        internal NeoEquatableClass? Equatable(string classId)
        {
            if (equatableClasses.TryGetValue(classId, out NeoEquatableClass? cached))
                return cached;
            NeoEquatableClass? entry = null;
            if (client.InterfaceImplementers.TryGetConstruction(classId, NeoPrimitiveInterfaces.IEquatableId, out var construction)
                && construction.TryGetValue(NeoPrimitiveInterfaces.IEquatableParameterId, out GenericBinding? argument))
            {
                entry = new NeoEquatableClass(
                    InstanceMember(classId, "Equals"),
                    InstanceMember(classId, "GetHashCode"),
                    argument);
            }
            equatableClasses[classId] = entry;
            return entry;
        }

        private string InstanceMember(string classId, string name) =>
            client.ResolveClassNode(classId).SurfaceMember(name)?.memberId
            ?? throw new NSGetterRuntimeError($"'{ClassName(classId)}' implements IEquatable but has no '{name}' member.");
    }

    /// <summary>P127 §7. How a class row runs its authored equality and hash.</summary>
    internal sealed class NeoEquatableClass
    {
        internal readonly string EqualsMemberId;
        internal readonly string HashCodeMemberId;
        /// <summary>IEquatable's construction argument: what <c>Equals</c> accepts.</summary>
        internal readonly GenericBinding Argument;

        internal NeoEquatableClass(string equalsMemberId, string hashCodeMemberId, GenericBinding argument)
        {
            EqualsMemberId = equalsMemberId;
            HashCodeMemberId = hashCodeMemberId;
            Argument = argument;
        }
    }

    public static partial class NSGetterEvaluator
    {
        private const string EqualsName = "Equals";
        private const string CompareToName = "CompareTo";
        private const string GetHashCodeName = "GetHashCode";
        private const string ToStringName = "ToString";

        private static readonly string[] Vector2Components = { "x", "y" };
        private static readonly string[] Vector3Components = { "x", "y", "z" };
        private static readonly string[] ColorComponents = { "r", "g", "b", "a" };

        /// <summary>The bits a float NaN hashes as, whatever its payload.</summary>
        private const int FloatNaNHash = 0x7FC00000;

        private static bool IsPrimitiveMemberName(string? name) =>
            name is EqualsName or CompareToName or GetHashCodeName or ToStringName;

        // ---------------------------------------------------------------
        // P127 §5. Primitive instance members, identical in both runtimes.
        // ---------------------------------------------------------------

        private static NSGetterRuntimeError PrimitiveArgumentError(string message) =>
            new(message, NeoErrorContract.NeoArgumentError);

        private static double RequirePrimitiveNumber(NeoPrimitive primitive, object? value)
        {
            if (!TryAsDouble(value, out double number))
                throw PrimitiveArgumentError($"Expected a {NeoPrimitiveInterfaces.NameOf(primitive)} value; got {ReceiverTypeName(value)}.");
            return number;
        }

        private static string RequirePrimitiveString(NeoPrimitive primitive, object? value)
        {
            if (value is not string text)
                throw PrimitiveArgumentError($"Expected a {NeoPrimitiveInterfaces.NameOf(primitive)} value; got {ReceiverTypeName(value)}.");
            return text;
        }

        private static bool RequirePrimitiveBool(object? value)
        {
            if (value is not bool flag)
                throw PrimitiveArgumentError($"Expected a bool value; got {ReceiverTypeName(value)}.");
            return flag;
        }

        private static double[] PrimitiveComponents(NeoPrimitive primitive, object? value)
        {
            string name = NeoPrimitiveInterfaces.NameOf(primitive);
            if (value is null)
                throw PrimitiveArgumentError($"Expected a {name} value; got null.");
            string[] names = primitive switch
            {
                NeoPrimitive.Color => ColorComponents,
                NeoPrimitive.Vector3 or NeoPrimitive.Vector3Int => Vector3Components,
                _ => Vector2Components,
            };
            var parts = new double[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                double component;
                bool read;
                if (primitive == NeoPrimitive.Color)
                {
                    read = TryReadColorComponent(value, names[i], out float channel);
                    component = channel;
                }
                else
                {
                    read = TryReadVectorComponent(value, names[i], out component);
                }
                if (!read)
                    throw PrimitiveArgumentError($"{name} value is missing numeric component '{names[i]}'.");
                parts[i] = component;
            }
            return parts;
        }

        private static bool IsIntComponents(NeoPrimitive primitive) =>
            primitive is NeoPrimitive.Vector2Int or NeoPrimitive.Vector3Int;

        /// <summary>C# <c>float.Equals</c>: like <c>==</c>, except NaN equals NaN.</summary>
        private static bool FloatEquals(double a, double b) =>
            a == b || (double.IsNaN(a) && double.IsNaN(b));

        /// <summary>C# <c>float.CompareTo</c>: NaN sorts below every number and equals NaN.</summary>
        private static int CompareFloats(double a, double b)
        {
            if (a < b)
                return -1;
            if (a > b)
                return 1;
            if (a == b)
                return 0;
            if (double.IsNaN(a))
                return double.IsNaN(b) ? 0 : -1;
            return 1;
        }

        /// <summary>JavaScript ToInt32: truncate, then wrap modulo 2^32.</summary>
        internal static int ToInt32(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;
            double wrapped = Math.Truncate(value) % 4294967296d;
            if (wrapped < 0)
                wrapped += 4294967296d;
            return unchecked((int)(uint)wrapped);
        }

        /// <summary>P127 §5. <c>ToInt32(h * 31 + c)</c> over UTF-16 code units from 0.</summary>
        private static int StringHash(string value)
        {
            int hash = 0;
            foreach (char unit in value)
                hash = unchecked(hash * 31 + unit);
            return hash;
        }

        private static int FloatHash(double value)
        {
            if (double.IsNaN(value))
                return FloatNaNHash;
            float single = (float)value;
            return single == 0 ? 0 : BitConverter.SingleToInt32Bits(single);
        }

        /// <summary>P127 §5. <c>ToInt32(h * 31 + part)</c> over the parts from 17.</summary>
        private static int CombineNeoHash(int hash, int part) => unchecked(hash * 31 + part);

        /// <summary>P127 §5. <c>value.Equals(other)</c> for a non-null receiver.</summary>
        private static bool PrimitiveEquals(NeoPrimitive primitive, object? value, object? other)
        {
            switch (primitive)
            {
                case NeoPrimitive.Int:
                case NeoPrimitive.Decimal:
                case NeoPrimitive.String:
                case NeoPrimitive.Bool:
                    return PrimitiveCompareTo(primitive, value, other) == 0;
                case NeoPrimitive.Float:
                    return FloatEquals(RequirePrimitiveNumber(primitive, value), RequirePrimitiveNumber(primitive, other));
                default:
                    {
                        double[] left = PrimitiveComponents(primitive, value);
                        double[] right = PrimitiveComponents(primitive, other);
                        for (int i = 0; i < left.Length; i++)
                        {
                            bool equal = primitive == NeoPrimitive.Color
                                ? FloatEquals(left[i], right[i])
                                : left[i] == right[i];
                            if (!equal)
                                return false;
                        }
                        return true;
                    }
            }
        }

        /// <summary>P127 §5. <c>value.CompareTo(other)</c>: -1, 0 or 1.</summary>
        private static int PrimitiveCompareTo(NeoPrimitive primitive, object? value, object? other)
        {
            switch (primitive)
            {
                case NeoPrimitive.Int:
                case NeoPrimitive.Float:
                    return CompareFloats(RequirePrimitiveNumber(primitive, value), RequirePrimitiveNumber(primitive, other));
                case NeoPrimitive.Decimal:
                    return Math.Sign(NeoDecimalMath.Compare(
                        RequirePrimitiveString(primitive, value),
                        RequirePrimitiveString(primitive, other)));
                case NeoPrimitive.String:
                    return Math.Sign(string.CompareOrdinal(
                        RequirePrimitiveString(primitive, value),
                        RequirePrimitiveString(primitive, other)));
                case NeoPrimitive.Bool:
                    return CompareFloats(RequirePrimitiveBool(value) ? 1 : 0, RequirePrimitiveBool(other) ? 1 : 0);
                default:
                    throw PrimitiveArgumentError($"{NeoPrimitiveInterfaces.NameOf(primitive)} has no CompareTo.");
            }
        }

        /// <summary>P127 §5. <c>value.GetHashCode()</c>, identical in both runtimes.</summary>
        private static int PrimitiveHashCode(NeoPrimitive primitive, object? value)
        {
            switch (primitive)
            {
                case NeoPrimitive.Int:
                    return ToInt32(RequirePrimitiveNumber(primitive, value));
                case NeoPrimitive.Float:
                    return FloatHash(RequirePrimitiveNumber(primitive, value));
                case NeoPrimitive.Decimal:
                    return StringHash(NeoSetEntryKey.DecimalKey(RequirePrimitiveString(primitive, value)));
                case NeoPrimitive.String:
                    return StringHash(RequirePrimitiveString(primitive, value));
                case NeoPrimitive.Bool:
                    return RequirePrimitiveBool(value) ? 1 : 0;
                default:
                    {
                        int hash = 17;
                        foreach (double part in PrimitiveComponents(primitive, value))
                            hash = CombineNeoHash(hash, IsIntComponents(primitive) ? ToInt32(part) : FloatHash(part));
                        return hash;
                    }
            }
        }

        /// <summary>P127 §5. <c>value.ToString(format)</c> in Unity's vector and color layout.</summary>
        private static string PrimitiveFormat(NeoPrimitive primitive, object? value, string format)
        {
            switch (primitive)
            {
                case NeoPrimitive.Int:
                    return NeoText.Format(NeoText.Int, RequirePrimitiveNumber(primitive, value), format);
                case NeoPrimitive.Float:
                    return NeoText.Format(NeoText.Float, RequirePrimitiveNumber(primitive, value), format);
                case NeoPrimitive.Decimal:
                    return NeoText.Format(NeoText.Decimal, RequirePrimitiveString(primitive, value), format);
                case NeoPrimitive.String:
                case NeoPrimitive.Bool:
                    throw PrimitiveArgumentError($"{NeoPrimitiveInterfaces.NameOf(primitive)} has no ToString(format).");
                default:
                    {
                        string number = IsIntComponents(primitive) ? NeoText.Int : NeoText.Float;
                        double[] parts = PrimitiveComponents(primitive, value);
                        var formatted = new string[parts.Length];
                        for (int i = 0; i < parts.Length; i++)
                            formatted[i] = NeoText.Format(number, parts[i], format);
                        string prefix = primitive == NeoPrimitive.Color ? "RGBA" : "";
                        return $"{prefix}({string.Join(", ", formatted)})";
                    }
            }
        }

        /// <summary>
        /// P127 §5. One primitive instance member over read arguments.
        /// <c>Equals(null)</c> is false, as C# answers for a non-null receiver.
        /// </summary>
        private static object? PrimitiveMember(NeoPrimitive primitive, string name, object? receiver, object?[] args)
        {
            object? argument = args.Length == 0 ? null : args[0];
            switch (name)
            {
                case EqualsName:
                    return Box(argument is not null && PrimitiveEquals(primitive, receiver, argument));
                case CompareToName:
                    return Box(PrimitiveCompareTo(primitive, receiver, argument));
                case GetHashCodeName:
                    return Box(PrimitiveHashCode(primitive, receiver));
                case ToStringName:
                    return PrimitiveFormat(primitive, receiver, RequirePrimitiveString(NeoPrimitive.String, argument));
                default:
                    throw new NSGetterRuntimeError($"'{name}' is not a primitive member.");
            }
        }

        /// <summary>Every decimal operand normalizes; ToString's format is a string.</summary>
        private static object? CallPrimitiveMember(NeoPrimitive primitive, string name, object? receiver, object?[] args)
        {
            if (primitive != NeoPrimitive.Decimal)
                return PrimitiveMember(primitive, name, receiver, args);
            object?[] operands = args;
            if (name != ToStringName)
            {
                operands = new object?[args.Length];
                for (int i = 0; i < args.Length; i++)
                    operands[i] = args[i] is null ? null : CoerceDecimalOperand(args[i], name);
            }
            return PrimitiveMember(primitive, name, CoerceDecimalOperand(receiver, name), operands);
        }

        private static NeoPrimitive? PrimitiveOfOp(string primitive) =>
            NeoPrimitiveInterfaces.TryParse(primitive, out NeoPrimitive parsed)
                ? parsed
                : throw new NSGetterRuntimeError($"Primitive op names unknown primitive '{primitive}'.");

        /// <summary>P127 §5. A primitive instance member on a closed primitive receiver.</summary>
        private static object? EvalPrimitiveOp(FunctionPrimitiveOpInfo info, NeoScriptScope scope, Context ctx)
        {
            var values = new object?[info.argPointers.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = EvalPointer(info.argPointers[i], scope, ctx);
            object? receiver = values.Length == 0 ? null : values[0];
            if (info.primitive == "tuple")
                return TupleOp(info.op, receiver, values.Length > 1 ? values[1] : null, ctx);
            string name = info.op switch
            {
                PrimitiveOpKind.EqualsOp => EqualsName,
                PrimitiveOpKind.CompareTo => CompareToName,
                PrimitiveOpKind.GetHashCode => GetHashCodeName,
                PrimitiveOpKind.Format => ToStringName,
                _ => throw new NSGetterRuntimeError($"Unknown primitive op '{info.op}'."),
            };
            var args = new object?[Math.Max(0, values.Length - 1)];
            Array.Copy(values, 1, args, 0, args.Length);
            return CallPrimitiveMember(PrimitiveOfOp(info.primitive)!.Value, name, receiver, args);
        }

        // ---------------------------------------------------------------
        // P127 §6. Primitive evidence: a non-row value behind a type
        // parameter or interface runs the primitive its evidence names.
        // ---------------------------------------------------------------

        /// <summary>The class of a value that is a class row, or null.</summary>
        private static string? ClassRowOf(object? value, Context ctx)
        {
            if (value is not (IDictionary<string, object?> or INeoValueReference))
                return null;
            string? classId = FindRowClassIdByReference(value, ctx);
            return string.IsNullOrEmpty(classId) ? null : classId;
        }

        /// <summary>A raw stored value id read as the value it names.</summary>
        private static object? ResolveHandle(object? value, Context ctx) =>
            value is string id ? ResolveValueIfId(id, ctx) : value;

        /// <summary>
        /// The primitive a non-row value holds, read from its evidence. Null
        /// for a class row or evidence that names no single primitive.
        /// </summary>
        private static NeoPrimitive? SourcePrimitive(TypeInfo? evidence, object? value, Context ctx)
        {
            if (evidence is null || value is null)
                return null;
            if (ClassRowOf(value, ctx) is not null)
                return null;
            return NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(evidence, ctx));
        }

        /// <summary>The P127 member a call on a non-row receiver names.</summary>
        private static string? InterfaceCallName(CallFunctionPointer pointer)
        {
            if (pointer.memberKey is not null)
                return pointer.memberKey;
            return pointer.memberId is null ? null : NeoPrimitiveInterfaces.MemberName(pointer.memberId);
        }

        private static string? TupleMemberOp(string? name) => name switch
        {
            EqualsName => PrimitiveOpKind.EqualsOp,
            CompareToName => PrimitiveOpKind.CompareTo,
            GetHashCodeName => PrimitiveOpKind.GetHashCode,
            _ => null,
        };

        /// <summary>
        /// P127 §5–§6. A call whose receiver is a primitive behind evidence,
        /// or a tuple, runs the intrinsic member instead of dispatching.
        /// </summary>
        internal static bool TryPrimitiveReceiverCall(
            CallFunctionPointer pointer,
            object? receiver,
            NeoScriptScope scope,
            Context ctx,
            out object? value)
        {
            value = null;
            if (SourcePrimitive(pointer.primitiveEvidence, receiver, ctx) is { } primitive)
            {
                string? name = InterfaceCallName(pointer);
                if (!IsPrimitiveMemberName(name))
                    throw new NSGetterRuntimeError($"Primitive evidence call '{name ?? pointer.callSiteId}' names no primitive member.");
                var args = new object?[pointer.args.Length];
                for (int i = 0; i < args.Length; i++)
                    args[i] = EvalPointer(pointer.args[i], scope, ctx);
                value = CallPrimitiveMember(primitive, name!, receiver, args);
                return true;
            }
            if (receiver is not RuntimeTuple || TupleMemberOp(InterfaceCallName(pointer)) is not { } op)
                return false;
            object? other = pointer.args.Length == 0 ? null : EvalPointer(pointer.args[0], scope, ctx);
            value = TupleOp(op, receiver, other, ctx);
            return true;
        }

        /// <summary>P127 §6. A non-row value conforms through the primitive its type names.</summary>
        private static bool PrimitiveImplementsInterface(object value, TypeInfo checkType, Context ctx) =>
            NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(checkType, ctx)) is { } primitive
            && RuntimeTypeCheck(value, NeoPrimitiveInterfaces.TypeInfoOf(primitive), ctx);

        /// <summary>P127 §6. Whether <paramref name="type"/>'s values are decimals, through evidence if open.</summary>
        internal static bool HoldsDecimal(TypeInfo type, Context ctx)
        {
            if (type.type == MemberKind.Decimal)
                return true;
            if (type.type is not (MemberKind.Generic or MemberKind.Interface))
                return false;
            return NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(type, ctx)) == NeoPrimitive.Decimal;
        }

        /// <summary>
        /// P127 §6. The float or decimal handling a search uses: the compiled
        /// flags, or the primitive an entry type parameter or interface resolves.
        /// </summary>
        private static NeoPrimitive? CollectionSearchKind(FunctionCollectionContainsInfo info, Context ctx)
        {
            if (info.@float)
                return NeoPrimitive.Float;
            if (info.@decimal)
                return NeoPrimitive.Decimal;
            if (info.evidence is null)
                return null;
            NeoPrimitive? kind = NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(info.evidence, ctx));
            return kind is NeoPrimitive.Float or NeoPrimitive.Decimal ? kind : null;
        }

        // ---------------------------------------------------------------
        // P127 §2/§4. `T.Name` and `T.Name(args)`: a static interface member
        // through T's binding.
        // ---------------------------------------------------------------

        /// <summary>P127 §2. A type parameter's source name, for runtime errors.</summary>
        private static string TypeParameterName(TypeInfo typeParameter, Context ctx)
        {
            if (NeoFunctionTypeArguments.IsFunctionGeneric(typeParameter))
            {
                int index = NeoFunctionTypeArguments.Index(typeParameter);
                string? ownerId = typeParameter switch
                {
                    FunctionGenericTypeInfo generic => generic.ownerMemberId,
                    FunctionArgumentTypeInfo argument => argument.ownerMemberId,
                    _ => null,
                };
                ctx.client.TryGetMember(ownerId ?? "", out JsonMember? owner);
                FunctionGenericParam[]? parameters = owner switch
                {
                    NSFunctionMember script => script.genericParams,
                    NSPropertyMember property => property.genericParams,
                    FunctionMember native => native.genericParams,
                    _ => null,
                };
                return parameters is not null && index < parameters.Length ? parameters[index].name : $"#{index}";
            }
            if (typeParameter is not GenericTypeInfo classParameter)
                return typeParameter.type.ToString();
            if (classParameter.ownerClassId is { } classId
                && ctx.client.classes.TryGetValue(classId, out NeoSchemaClass? owningClass)
                && owningClass.genericParams?.Find(parameter => parameter.id == classParameter.genericParamId) is { } declared)
            {
                return declared.name;
            }
            return classParameter.genericParamId;
        }

        private static object? EvalTypeParameterStatic(TypeParameterStaticPointer pointer, NeoScriptScope scope, Context ctx)
        {
            TypeInfo binding = CloseContextType(pointer.typeParameter, ctx);
            // A field initializer runs before its row has a receiver.
            if (ctx.thisValue is null
                && ctx.initializerGenericEnv is { } env
                && NeoNSFunctionRuntime.ContainsGeneric(binding))
                binding = NeoGenericResolution.SubstituteTypeInfo(ctx.client, binding, env);
            if (binding is ClassTypeInfo classType)
            {
                JsonMember member = ctx.client.PrimitiveInterfaceSchema.StaticImplementation(
                    classType.classId,
                    pointer.memberId,
                    () => TypeParameterName(pointer.typeParameter, ctx));
                return EvalPointer(StaticImplementationPointer(member, pointer), scope, ctx);
            }
            var args = new object?[pointer.args?.Length ?? 0];
            for (int i = 0; i < args.Length; i++)
                args[i] = EvalPointer(pointer.args![i], scope, ctx);
            return PrimitiveStaticMember(binding, pointer, args, ctx);
        }

        /// <summary>The ordinary static read or call that runs <paramref name="member"/>, cached per pointer.</summary>
        private static Pointer StaticImplementationPointer(JsonMember member, TypeParameterStaticPointer pointer)
        {
            Dictionary<string, Pointer>? current = pointer.implementations;
            if (current is not null && current.TryGetValue(member.id, out Pointer? cached))
                return cached;
            CallReceiver receiver = CallReceiver.Static(member.id);
            Pointer call;
            if (pointer.args is not null)
            {
                call = new CallFunctionPointer
                {
                    memberId = member.id,
                    receiver = receiver,
                    args = pointer.args,
                    callSiteId = pointer.callSiteId ?? member.id,
                };
            }
            else if (member is NSPropertyMember)
            {
                call = new CallGetterPointer { memberId = member.id, receiver = receiver };
            }
            else
            {
                call = new StaticMemberPointer { memberId = member.id };
            }
            // Copy on write: a site shared across threads reads a whole map.
            var next = current is null
                ? new Dictionary<string, Pointer>(StringComparer.Ordinal)
                : new Dictionary<string, Pointer>(current, StringComparer.Ordinal);
            next[member.id] = call;
            pointer.implementations = next;
            return call;
        }

        /// <summary>P127 §4. The primitive intrinsic table, keyed by canonical member name.</summary>
        private static object? PrimitiveStaticMember(
            TypeInfo binding,
            TypeParameterStaticPointer pointer,
            object?[] args,
            Context ctx)
        {
            MemberKind kind = binding.type;
            string? name = NeoPrimitiveInterfaces.MemberName(pointer.memberId);
            ArithmeticNumeric? numeric = kind switch
            {
                MemberKind.Int => ArithmeticNumeric.Int,
                MemberKind.Float => ArithmeticNumeric.Float,
                _ => null,
            };
            bool isDecimal = kind == MemberKind.Decimal;
            object? first = args.Length > 0 ? args[0] : null;
            object? second = args.Length > 1 ? args[1] : null;
            if (numeric is not null || isDecimal)
            {
                switch (name)
                {
                    case "Zero":
                    case "AdditiveIdentity":
                        return isDecimal ? "0" : Box(0d);
                    case "One":
                    case "MultiplicativeIdentity":
                        return isDecimal ? "1" : Box(1d);
                    case "op_Addition":
                        return ApplyArithmetic(ArithmeticOpKind.Addition, new[] { first, second }, isDecimal, numeric, ctx);
                    case "op_Subtraction":
                        return ApplyArithmetic(ArithmeticOpKind.Subtraction, new[] { first, second }, isDecimal, numeric, ctx);
                    case "op_Multiply":
                        return ApplyArithmetic(ArithmeticOpKind.Multiplication, new[] { first, second }, isDecimal, numeric, ctx);
                    case "op_UnaryNegation":
                        return ApplyArithmetic(ArithmeticOpKind.Multiplication, new[] { Box(-1d), first }, isDecimal, numeric, ctx);
                    case "op_UnaryPlus":
                        return first;
                    case "op_Increment":
                        return ApplyArithmetic(ArithmeticOpKind.Addition, new[] { first, Box(1d) }, isDecimal, numeric, ctx);
                    case "op_Decrement":
                        return ApplyArithmetic(ArithmeticOpKind.Subtraction, new[] { first, Box(1d) }, isDecimal, numeric, ctx);
                }
            }
            if (numeric is not null)
            {
                switch (name)
                {
                    case "op_Division":
                        return ApplyArithmetic(ArithmeticOpKind.Division, new[] { first, second }, false, numeric, ctx);
                    case "op_Modulus":
                        return ApplyArithmetic(ArithmeticOpKind.Remainder, new[] { first, second }, false, numeric, ctx);
                }
            }
            if (kind == MemberKind.Int)
            {
                string? bitwise = name switch
                {
                    "op_BitwiseAnd" => "and",
                    "op_BitwiseOr" => "or",
                    "op_ExclusiveOr" => "xor",
                    "op_OnesComplement" => "not",
                    "op_LeftShift" => "shiftLeft",
                    "op_RightShift" => "shiftRight",
                    "op_UnsignedRightShift" => "shiftRightUnsigned",
                    _ => null,
                };
                if (bitwise is not null)
                {
                    int left = BitwiseOperand(bitwise, 0, first, ctx);
                    return bitwise == "not"
                        ? Box((double)~left)
                        : Box(ApplyBitwiseOp(bitwise, left, BitwiseOperand(bitwise, 1, second, ctx)));
                }
            }
            if (name is "Parse" or "TryParse" && PrimitiveParse(kind, name, args) is { } parsed)
                return parsed.value;
            throw new NSGetterRuntimeError(
                $"Type parameter '{TypeParameterName(pointer.typeParameter, ctx)}' is bound to '{DescribeRuntimeType(binding, ctx)}', which has no static member '{NeoPrimitiveInterfaces.DisplayName(name ?? ctx.client.PrimitiveInterfaceSchema.InterfaceMemberName(pointer.memberId))}'.");
        }

        /// <summary>P127 §4. <c>Parse</c> and <c>TryParse</c> through the existing primitive statics.</summary>
        private static (object? value, bool _)? PrimitiveParse(MemberKind kind, string name, object?[] args)
        {
            if (kind == MemberKind.String)
            {
                if ((args.Length > 0 ? args[0] : null) is not string text)
                    throw PrimitiveArgumentError($"{name} requires a string argument.");
                return (text, true);
            }
            string? number = kind switch
            {
                MemberKind.Int => NeoText.Int,
                MemberKind.Float => NeoText.Float,
                MemberKind.Decimal => NeoText.Decimal,
                MemberKind.Bool => NeoText.Bool,
                _ => null,
            };
            if (number is null)
                return null;
            string source = TextStringArgument(args, 0, $"{number}.{name}");
            return (name == "Parse" ? NeoText.Parse(number, source) : NeoText.TryParse(number, source), true);
        }

        // ---------------------------------------------------------------
        // P127 §4/§5. Tuple members, element by element.
        // ---------------------------------------------------------------

        private static object TupleOp(string op, object? receiver, object? other, Context ctx)
        {
            if (receiver is not RuntimeTuple tuple)
                throw new NSGetterRuntimeError($"Tuple {op} receiver is not a tuple.");
            return op switch
            {
                PrimitiveOpKind.EqualsOp => Box(TupleEquals(tuple, other, ctx)),
                PrimitiveOpKind.CompareTo => Box(TupleCompareTo(tuple, other, ctx)),
                PrimitiveOpKind.GetHashCode => Box(TupleHashCode(tuple, ctx)),
                _ => throw new NSGetterRuntimeError("A tuple has no ToString(format)."),
            };
        }

        private static bool TupleEquals(RuntimeTuple tuple, object? other, Context ctx)
        {
            if (other is not RuntimeTuple right)
                return false;
            TupleTypeInfo type = RuntimeTupleType(tuple.TypeInfo, ctx);
            for (int i = 0; i < type.elements.Length; i++)
            {
                object? a = tuple[i];
                object? b = right[i];
                bool equal;
                if (a is null || b is null)
                    equal = a is null && b is null;
                else if (a is RuntimeTuple nested)
                    equal = TupleEquals(nested, b, ctx);
                else if (ClassRowOf(a, ctx) is var leftClass && ClassRowOf(b, ctx) is var rightClass
                    && (leftClass is not null || rightClass is not null))
                    equal = RowEquality(a, leftClass, b, rightClass, ctx);
                else
                    equal = CallPrimitiveMember(TupleElementPrimitive(type.elements[i].typeInfo, EqualsName, ctx), EqualsName, a, new[] { b }) is true;
                if (!equal)
                    return false;
            }
            return true;
        }

        /// <summary>Left to right, each element's CompareTo; a null element sorts first.</summary>
        private static int TupleCompareTo(RuntimeTuple tuple, object? other, Context ctx)
        {
            if (other is not RuntimeTuple right)
                throw new NSGetterRuntimeError("Tuple CompareTo argument is not a tuple.");
            TupleTypeInfo type = RuntimeTupleType(tuple.TypeInfo, ctx);
            for (int i = 0; i < type.elements.Length; i++)
            {
                int order = TupleElementCompareTo(tuple[i], right[i], type.elements[i].typeInfo, ctx);
                if (order != 0)
                    return order;
            }
            return 0;
        }

        private static int TupleElementCompareTo(object? a, object? b, TypeInfo type, Context ctx)
        {
            if (a is null)
                return b is null ? 0 : -1;
            if (b is null)
                return 1;
            if (a is RuntimeTuple nested)
                return TupleCompareTo(nested, b, ctx);
            if (ClassRowOf(a, ctx) is not null)
                return CompareTo(a, b, null, ctx);
            return PrimitiveCompareTo(TupleElementPrimitive(type, CompareToName, ctx), a, b);
        }

        /// <summary>P127 §5. <c>h = ToInt32(h * 31 + elementHash)</c> from 17; null hashes as 0.</summary>
        private static int TupleHashCode(RuntimeTuple tuple, Context ctx)
        {
            TupleTypeInfo type = RuntimeTupleType(tuple.TypeInfo, ctx);
            int hash = 17;
            for (int i = 0; i < type.elements.Length; i++)
            {
                object? value = tuple[i];
                int part;
                if (value is null)
                    part = 0;
                else if (value is RuntimeTuple nested)
                    part = TupleHashCode(nested, ctx);
                else if (ClassRowOf(value, ctx) is { } classId)
                {
                    NeoEquatableClass equatable = ctx.client.PrimitiveInterfaceSchema.Equatable(classId)
                        ?? throw new NSGetterRuntimeError(
                            $"Tuple GetHashCode element '{ctx.client.PrimitiveInterfaceSchema.ClassName(classId)}' isn't IEquatable.");
                    part = EquatableHash(value, equatable, ctx);
                }
                else
                    part = PrimitiveHashCode(TupleElementPrimitive(type.elements[i].typeInfo, GetHashCodeName, ctx), value);
                hash = CombineNeoHash(hash, part);
            }
            return hash;
        }

        /// <summary>The primitive a non-row tuple element of <paramref name="type"/> holds.</summary>
        private static NeoPrimitive TupleElementPrimitive(TypeInfo type, string member, Context ctx) =>
            NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(type, ctx))
            ?? throw new NSGetterRuntimeError($"Tuple {member} reached an element of kind {type.type}, which has none.");

        // ---------------------------------------------------------------
        // P127 §7. IEquatable classes: value equality through the class's
        // own Equals and GetHashCode.
        // ---------------------------------------------------------------

        /// <summary>The class whose CompareTo or Equals a row runs, or a precise failure.</summary>
        private static string RowMember(object row, string name, Context ctx)
        {
            string? classId = ClassRowOf(row, ctx)
                ?? throw new NSGetterRuntimeError($"Cannot resolve interface Function member '{name}' because the receiver has no runtime class.");
            return ctx.client.ResolveClassNode(classId).SurfaceMember(name)?.memberId
                ?? throw new NSGetterRuntimeError($"Runtime class '{classId}' does not implement Function member '{name}'.");
        }

        private static bool InvokeEquals(NeoEquatableClass equatable, object receiver, object other, Context ctx) =>
            NeoNSFunctionRuntime.InvokeImmediate(ctx.client, equatable.EqualsMemberId, receiver, new[] { other }, ctx) is true;

        /// <summary>ToInt32 of an equatable row's authored <c>GetHashCode()</c>.</summary>
        private static int EquatableHash(object row, NeoEquatableClass equatable, Context ctx)
        {
            object? hash = NeoNSFunctionRuntime.InvokeImmediate(ctx.client, equatable.HashCodeMemberId, row, Array.Empty<object?>(), ctx);
            if (!TryAsDouble(hash, out double number))
                throw new NSGetterRuntimeError("GetHashCode must return an int.");
            return ToInt32(number);
        }

        /// <summary>
        /// P127 §8. <c>left.CompareTo(right)</c> for a <c>comparable</c> order
        /// key: a row's authored member, else the primitive the evidence names.
        /// </summary>
        private static int CompareTo(object left, object? right, TypeInfo? evidence, Context ctx)
        {
            if (left is RuntimeTuple tuple)
                return TupleCompareTo(tuple, right, ctx);
            if (SourcePrimitive(evidence, left, ctx) is { } primitive)
                return PrimitiveCompareTo(primitive, left, right);
            object? order = NeoNSFunctionRuntime.InvokeImmediate(
                ctx.client,
                RowMember(left, CompareToName, ctx),
                left,
                new[] { right },
                ctx);
            if (!TryAsDouble(order, out double number))
                throw new NSGetterRuntimeError("CompareTo must return a number.");
            if (!NeoNumbers.IsWhole(number))
                throw new NSGetterRuntimeError($"CompareTo returned {NeoNumbers.Format(number)}, not an int.");
            return Math.Sign(number);
        }

        /// <summary>Whether two values name one row: the same instance or one stored value.</summary>
        private static bool SameRow(object a, object b, Context ctx)
        {
            if (ReferenceEquals(a, b))
                return true;
            object? key = ReferenceKeyOf(a, ctx);
            return key is not null && key.Equals(ReferenceKeyOf(b, ctx));
        }

        /// <summary>
        /// P127 §7 step 4. <c>a.Equals(b)</c> when <c>a</c>'s IEquatable admits
        /// <c>b</c>, else <c>b.Equals(a)</c> when <c>b</c>'s admits <c>a</c>.
        /// Null when either row's class is not equatable.
        /// </summary>
        private static bool? EquatableRowsEqual(object a, string leftClassId, object b, string rightClassId, Context ctx)
        {
            NeoPrimitiveInterfaceSchema schema = ctx.client.PrimitiveInterfaceSchema;
            if (schema.Equatable(leftClassId) is not { } left)
                return null;
            if (schema.Equatable(rightClassId) is not { } right)
                return null;
            if (EquatableAdmits(a, left, b, ctx))
                return InvokeEquals(left, a, b, ctx);
            if (EquatableAdmits(b, right, a, ctx))
                return InvokeEquals(right, b, a, ctx);
            return false;
        }

        private static bool EquatableAdmits(object row, NeoEquatableClass equatable, object other, Context ctx)
        {
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env = equatable.Argument.kind == NeoGenericBindingKind.Member
                ? NeoGenericResolution.EmptyEnv
                : NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, row, ctx, "An IEquatable argument");
            TypeInfo accepted = NeoGenericResolution.TypeInfoFromArgumentSignature(
                NeoGenericResolution.SignatureOfBinding(ctx.client, equatable.Argument, env));
            return RuntimeTypeCheck(other, accepted, ctx);
        }

        /// <summary>
        /// P127 §12. CLR <c>Equals</c> between two generated class values,
        /// each a stored value id or a detached object: §7 row equality.
        /// </summary>
        internal static bool GeneratedValueEquality(object left, object right, Context ctx)
        {
            object? a = ResolveHandle(left, ctx);
            object? b = ResolveHandle(right, ctx);
            return RowEquality(a, ClassRowOf(a, ctx), b, ClassRowOf(b, ctx), ctx);
        }

        /// <summary>
        /// P127 §7 steps 2, 4 and 5 for operands of which at least one is a
        /// class row: identity unless both rows are equatable.
        /// </summary>
        private static bool RowEquality(object? a, string? leftClassId, object? b, string? rightClassId, Context ctx)
        {
            if (leftClassId is null || rightClassId is null)
                return false;
            if (SameRow(a!, b!, ctx))
                return true;
            return EquatableRowsEqual(a!, leftClassId, b!, rightClassId, ctx) ?? false;
        }

        /// <summary>
        /// P127 §7 step 3. Predefined <c>==</c> for two non-row operands whose
        /// evidence resolves a primitive kind; <c>int</c> widens to
        /// <c>float</c> or <c>decimal</c>, and any other pair of kinds is
        /// unequal. Null when neither side resolves.
        /// </summary>
        private static bool? PrimitiveEquality(object a, object b, TypeInfo[] evidence, Context ctx)
        {
            NeoPrimitive? left = NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(evidence[0], ctx));
            NeoPrimitive? right = NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(evidence[1], ctx));
            if (left is null && right is null)
                return null;
            if (left is not null && right is not null && left != right)
            {
                NeoPrimitive? widened = WidenedEqualityKind(left.Value, right.Value);
                if (widened is null)
                    return false;
                return widened == NeoPrimitive.Decimal ? DecimalEqual(a, b) : JsEqual(a, b, ctx);
            }
            return (left ?? right) == NeoPrimitive.Decimal ? DecimalEqual(a, b) : JsEqual(a, b, ctx);
        }

        private static NeoPrimitive? WidenedEqualityKind(NeoPrimitive left, NeoPrimitive right)
        {
            NeoPrimitive? other = left == NeoPrimitive.Int ? right : right == NeoPrimitive.Int ? left : null;
            return other is NeoPrimitive.Float or NeoPrimitive.Decimal ? other : null;
        }

        private static bool DecimalEqual(object a, object b) =>
            NeoDecimalMath.Compare(CoerceDecimalOperand(a, "comparison"), CoerceDecimalOperand(b, "comparison")) == 0;

        /// <summary>P127 §7. <c>==</c> on operands that carry primitive evidence.</summary>
        private static bool EvidenceEquality(object? a, object? b, TypeInfo[] evidence, Context ctx)
        {
            // P112 §3.5. A `Reference<T>` beside a `T` compares the value it names.
            object? x = ReadThroughReference(a, ctx);
            object? y = ReadThroughReference(b, ctx);
            if (x is null || y is null)
                return x is null && y is null;
            string? left = ClassRowOf(x, ctx);
            string? right = ClassRowOf(y, ctx);
            if (left is not null || right is not null)
                return RowEquality(x, left, y, right, ctx);
            return PrimitiveEquality(x, y, evidence, ctx) ?? JsEqual(x, y, ctx);
        }

        /// <summary>
        /// P127 §7. Buckets equatable rows by <c>GetHashCode</c> so rows that
        /// are <c>Equals</c> share the first one's set key. Each lookup runs
        /// <c>Equals</c> only within one bucket.
        /// </summary>
        internal sealed class EquatableSetKeys
        {
            private readonly Dictionary<int, List<(object value, string classId, object key)>> buckets = new();

            internal object Key(object value, string classId, object identity, Context ctx)
            {
                if (ctx.client.PrimitiveInterfaceSchema.Equatable(classId) is not { } equatable)
                    return identity;
                int hash = EquatableHash(value, equatable, ctx);
                if (!buckets.TryGetValue(hash, out var bucket))
                {
                    bucket = new List<(object value, string classId, object key)>();
                    buckets.Add(hash, bucket);
                }
                foreach (var representative in bucket)
                {
                    if (RowEquality(representative.value, representative.classId, value, classId, ctx))
                        return representative.key;
                }
                bucket.Add((value, classId, identity));
                return identity;
            }
        }

        /// <summary>P127 §7. A class row's set key: the key of the first row it <c>Equals</c>.</summary>
        private static object? EquatableEntryKey(EquatableSetKeys? keys, object? entry, object? identity, bool reference, Context ctx)
        {
            if (keys is null || entry is null)
                return identity;
            object? value = reference ? ResolveHandle(entry, ctx) : entry;
            if (value is null || ClassRowOf(value, ctx) is not { } classId)
                return identity;
            return keys.Key(value, classId, identity ?? value, ctx);
        }

        /// <summary>
        /// P127 §7. A stored Set keys class entries by row, so it refuses a
        /// row, or a tuple element row, of an equatable class.
        /// </summary>
        internal static void AssertStoredSetEntryNotEquatable(object? value, Context ctx)
        {
            NeoPrimitiveInterfaceSchema schema = ctx.client.PrimitiveInterfaceSchema;
            if (!schema.AnyEquatable)
                return;
            if (value is RuntimeTuple tuple)
            {
                foreach (object? element in tuple)
                    AssertStoredSetEntryNotEquatable(element, ctx);
                return;
            }
            if (ClassRowOf(ResolveHandle(value, ctx), ctx) is not { } classId || schema.Equatable(classId) is null)
                return;
            throw new NSGetterRuntimeError(
                $"Set Add: `Set` elements can't hold `IEquatable` class '{schema.ClassName(classId)}', since a stored Set keys entries by row. Use a `List`.");
        }

        /// <summary>P127 §7. A tuple handle element's hash when its row's class is equatable.</summary>
        private static int? EquatableHandleHash(object value, Context ctx)
        {
            if (!ctx.client.PrimitiveInterfaceSchema.AnyEquatable)
                return null;
            object? row = ResolveHandle(value, ctx);
            if (row is null || ClassRowOf(row, ctx) is not { } classId)
                return null;
            return ctx.client.PrimitiveInterfaceSchema.Equatable(classId) is { } equatable
                ? EquatableHash(row, equatable, ctx)
                : null;
        }

        /// <summary>P127 §6. A non-row tuple element whose interface resolves <c>decimal</c>.</summary>
        private static bool IsDecimalEvidence(object value, TypeInfo type, Context ctx) =>
            NeoPrimitiveInterfaces.AdmitsPrimitives(type)
            && ClassRowOf(value, ctx) is null
            && NeoPrimitiveInterfaces.OfTypeInfo(CloseContextType(type, ctx)) == NeoPrimitive.Decimal;

        /// <summary>The search value's class when that class is equatable.</summary>
        private static string? EquatableSearchClass(object? target, Context ctx)
        {
            if (target is null || !ctx.client.PrimitiveInterfaceSchema.AnyEquatable)
                return null;
            string? classId = ClassRowOf(ResolveHandle(target, ctx), ctx);
            return classId is not null && ctx.client.PrimitiveInterfaceSchema.Equatable(classId) is not null ? classId : null;
        }
    }
}
