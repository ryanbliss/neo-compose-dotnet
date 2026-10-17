// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>How an interface fixes an implementing class's own params (P113 §1).</summary>
    internal enum NeoInterfaceDeterminationKind
    {
        /// <summary>The class has no open params.</summary>
        Closed,

        /// <summary>Each open param fills exactly one interface position, bare, with its constraint.</summary>
        Determined,

        NotImplemented,
        UnboundParam,
        RepeatedParam,
        NarrowedConstraint,
    }

    internal sealed class NeoInterfaceDetermination
    {
        internal static readonly NeoInterfaceDetermination Closed =
            new(NeoInterfaceDeterminationKind.Closed);

        internal static readonly NeoInterfaceDetermination NotImplemented =
            new(NeoInterfaceDeterminationKind.NotImplemented);

        internal NeoInterfaceDetermination(
            NeoInterfaceDeterminationKind kind,
            GenericParamDeclaration? param = null,
            GenericParamDeclaration? position = null,
            IReadOnlyDictionary<string, string>? paramByPosition = null)
        {
            this.kind = kind;
            this.param = param;
            this.position = position;
            this.paramByPosition = paramByPosition;
        }

        internal NeoInterfaceDeterminationKind kind
        {
            get;
        }

        /// <summary>The class param an undetermined result names.</summary>
        internal GenericParamDeclaration? param
        {
            get;
        }

        /// <summary>The interface position a narrowed constraint names.</summary>
        internal GenericParamDeclaration? position
        {
            get;
        }

        /// <summary>Determined only: interface param id → the class param it fills.</summary>
        internal IReadOnlyDictionary<string, string>? paramByPosition
        {
            get;
        }

        internal bool Admits => kind is NeoInterfaceDeterminationKind.Closed
            or NeoInterfaceDeterminationKind.Determined;
    }

    /// <summary>
    /// The P113 §3.5 implementer index, built once per schema: each class's
    /// implemented interface constructions and each interface's concrete
    /// closed-or-determined implementers. Mirrors the web model's
    /// <c>InterfaceImplementerIndex</c>.
    /// </summary>
    internal sealed class NeoInterfaceImplementers
    {
        private static readonly IReadOnlyDictionary<string, GenericBinding> EmptyConstruction =
            new Dictionary<string, GenericBinding>();

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>> EmptyMap =
            new Dictionary<string, IReadOnlyDictionary<string, GenericBinding>>();

        private static readonly IReadOnlyList<string> EmptyIds = new List<string>();

        private static readonly List<GenericParamDeclaration> EmptyScope = new();

        private readonly NeoClient client;
        private readonly ProjectData data;
        private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>>>
            closureByInterfaceId = new();
        private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>>>
            mapByClassId = new();
        private readonly Dictionary<(string, string), NeoInterfaceDetermination> determinationByKey = new();
        private readonly Dictionary<string, List<GenericParamDeclaration>> scopeByClassId = new();
        private readonly Dictionary<(ClassMember, string), IReadOnlyDictionary<string, GenericBinding>?>
            slotArgumentsByKey = new();
        private readonly HashSet<string> visiting = new();
        private Dictionary<string, List<string>>? implementersByInterfaceId;

        internal NeoInterfaceImplementers(NeoClient client, ProjectData data)
        {
            this.client = client;
            this.data = data;
        }

        /// <summary>Every interface <paramref name="interfaceId"/> is or extends, with its arguments.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>> InterfaceClosure(
            string interfaceId)
        {
            if (closureByInterfaceId.TryGetValue(interfaceId, out var cached))
                return cached;
            if (!data.interfaces.TryGetValue(interfaceId, out Interface? declaration))
                return EmptyMap;
            string key = "i:" + interfaceId;
            // A cyclic `extends` is refused by document validation; here it ends.
            if (!visiting.Add(key))
                return EmptyMap;
            var closure = new Dictionary<string, IReadOnlyDictionary<string, GenericBinding>>
            {
                [interfaceId] = IdentityConstruction(declaration.genericParams),
            };
            if (declaration.extendsInterfaceIds is not null)
            {
                foreach (string parentId in declaration.extendsInterfaceIds)
                {
                    IReadOnlyDictionary<string, GenericBinding> parentArguments = EmptyConstruction;
                    if (declaration.extendsInterfaceArguments?.TryGetValue(parentId, out var declared) == true)
                        parentArguments = declared;
                    foreach (var pair in InterfaceClosure(parentId))
                        closure.TryAdd(pair.Key, Substitute(pair.Value, parentArguments));
                }
            }
            visiting.Remove(key);
            closureByInterfaceId[interfaceId] = closure;
            return closure;
        }

        /// <summary>Every interface <paramref name="classId"/> implements, with its arguments.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>> Constructions(
            string classId)
        {
            if (mapByClassId.TryGetValue(classId, out var cached))
                return cached;
            if (!data.classes.TryGetValue(classId, out NeoSchemaClass? schemaClass))
                return EmptyMap;
            string key = "c:" + classId;
            if (!visiting.Add(key))
                return EmptyMap;
            var parentMap = schemaClass.extendsClassId is null
                ? EmptyMap
                : Constructions(schemaClass.extendsClassId);
            var bindings = schemaClass.extendsGenericBindings;
            var result = parentMap;
            if (schemaClass.implementsInterfaceIds is { Count: > 0 } || ForwardsAnyBoundParam(parentMap, bindings))
            {
                var map = new Dictionary<string, IReadOnlyDictionary<string, GenericBinding>>(parentMap.Count);
                foreach (var pair in parentMap)
                    map[pair.Key] = bindings is null ? pair.Value : Substitute(pair.Value, bindings);
                if (schemaClass.implementsInterfaceIds is not null)
                {
                    foreach (string implementedId in schemaClass.implementsInterfaceIds)
                    {
                        IReadOnlyDictionary<string, GenericBinding> implementedArguments = EmptyConstruction;
                        if (schemaClass.implementsInterfaceArguments?.TryGetValue(implementedId, out var declared) == true)
                            implementedArguments = declared;
                        foreach (var pair in InterfaceClosure(implementedId))
                            map.TryAdd(pair.Key, Substitute(pair.Value, implementedArguments));
                    }
                }
                result = map;
            }
            visiting.Remove(key);
            mapByClassId[classId] = result;
            return result;
        }

        /// <summary>The arguments <paramref name="classId"/> implements <paramref name="interfaceId"/> with.</summary>
        internal bool TryGetConstruction(
            string classId,
            string interfaceId,
            out IReadOnlyDictionary<string, GenericBinding> construction) =>
            Constructions(classId).TryGetValue(interfaceId, out construction!);

        internal bool Implements(string classId, string interfaceId) =>
            Constructions(classId).ContainsKey(interfaceId);

        /// <summary>P113 §1: is <paramref name="classId"/> closed, or determined by <paramref name="interfaceId"/>?</summary>
        internal NeoInterfaceDetermination Determination(string classId, string interfaceId)
        {
            var key = (classId, interfaceId);
            if (!determinationByKey.TryGetValue(key, out NeoInterfaceDetermination? result))
            {
                result = ComputeDetermination(classId, interfaceId);
                determinationByKey[key] = result;
            }
            return result;
        }

        /// <summary>
        /// Concrete classes that implement <paramref name="interfaceId"/> and
        /// that it closes or determines, in document order.
        /// </summary>
        internal IReadOnlyList<string> Implementers(string interfaceId)
        {
            implementersByInterfaceId ??= BuildImplementers();
            return implementersByInterfaceId.TryGetValue(interfaceId, out List<string>? ids)
                ? ids
                : EmptyIds;
        }

        /// <summary>
        /// P113 §1. The class arguments a row of <paramref name="rowClassId"/>
        /// takes from <paramref name="slot"/>: a class slot's own arguments,
        /// the slot's interface arguments read by position for a row its
        /// interface determines, and none for a closed row.
        /// </summary>
        internal IReadOnlyDictionary<string, GenericBinding>? SlotClassArguments(
            ClassMember slot,
            string rowClassId)
        {
            if (slot.DeclaredInterfaceId is not { } interfaceId)
                return slot.classArguments;
            if (slot.interfaceArguments is not { } slotArguments)
                return null;
            var key = (slot, rowClassId);
            if (slotArgumentsByKey.TryGetValue(key, out var cached))
                return cached;
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null;
            if (Determination(rowClassId, interfaceId).paramByPosition is { } paramByPosition)
            {
                var byClassParam = new Dictionary<string, GenericBinding>(paramByPosition.Count);
                foreach (var pair in paramByPosition)
                {
                    if (slotArguments.TryGetValue(pair.Key, out GenericBinding? binding))
                        byClassParam[pair.Value] = binding;
                }
                classArguments = byClassParam;
            }
            slotArgumentsByKey[key] = classArguments;
            return classArguments;
        }

        /// <summary>
        /// P113 §3.3. Null when the interface closes or determines
        /// <paramref name="rowClassId"/>, else why <paramref name="slotLabel"/>
        /// can't hold its row.
        /// </summary>
        internal string? UndeterminedRowMessage(string slotLabel, string rowClassId, string interfaceId)
        {
            NeoInterfaceDetermination determination = Determination(rowClassId, interfaceId);
            if (determination.Admits)
                return null;
            string rowName = ClassName(rowClassId);
            return $"{slotLabel} can't hold a {rowName}, because {Reason(determination, rowName, interfaceId)}.";
        }

        internal string InterfaceName(string interfaceId) =>
            data.interfaces.TryGetValue(interfaceId, out Interface? declaration)
                ? declaration.name
                : interfaceId;

        private string ClassName(string classId) =>
            data.classes.TryGetValue(classId, out NeoSchemaClass? schemaClass)
                ? schemaClass.name
                : classId;

        private string Reason(NeoInterfaceDetermination determination, string rowName, string interfaceId)
        {
            string interfaceName = InterfaceName(interfaceId);
            switch (determination.kind)
            {
                case NeoInterfaceDeterminationKind.NotImplemented:
                    return $"{rowName} doesn't implement {interfaceName}";
                case NeoInterfaceDeterminationKind.NarrowedConstraint:
                    return $"{rowName}'s {ConstraintClause(determination.param!)}, and {interfaceName}'s {ConstraintClause(determination.position!)}";
                default:
                    return $"{interfaceName} doesn't determine {rowName}'s parameter {determination.param!.name}";
            }
        }

        private string ConstraintClause(GenericParamDeclaration param)
        {
            GenericParamConstraint? constraint = param.constraint;
            if (constraint is null)
                return $"{param.name} is unconstrained";
            switch (constraint.kind)
            {
                case NeoGenericParamConstraintKind.Enum:
                    return $"{param.name} extends {(data.enums.TryGetValue(constraint.enumId!, out Enum? declared) ? declared.name : constraint.enumId)}";
                case NeoGenericParamConstraintKind.Interface:
                    return $"{param.name} extends {InterfaceName(constraint.interfaceId!)}";
                default:
                    return $"{param.name} extends {ClassName(constraint.classId!)}";
            }
        }

        private Dictionary<string, List<string>> BuildImplementers()
        {
            var byInterface = new Dictionary<string, List<string>>();
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                if (schemaClass.Modifier == NeoClassModifierKind.Abstract)
                    continue;
                foreach (string interfaceId in Constructions(schemaClass.id).Keys)
                {
                    if (!Determination(schemaClass.id, interfaceId).Admits)
                        continue;
                    if (!byInterface.TryGetValue(interfaceId, out List<string>? ids))
                    {
                        ids = new List<string>();
                        byInterface[interfaceId] = ids;
                    }
                    ids.Add(schemaClass.id);
                }
            }
            return byInterface;
        }

        private NeoInterfaceDetermination ComputeDetermination(string classId, string interfaceId)
        {
            if (!TryGetConstruction(classId, interfaceId, out var construction))
                return NeoInterfaceDetermination.NotImplemented;
            List<GenericParamDeclaration> scope = OpenScope(classId);
            if (scope.Count == 0)
                return NeoInterfaceDetermination.Closed;
            var positionById = new Dictionary<string, GenericParamDeclaration>();
            if (data.interfaces.TryGetValue(interfaceId, out Interface? declaration)
                && declaration.genericParams is not null)
            {
                foreach (GenericParamDeclaration position in declaration.genericParams)
                    positionById[position.id] = position;
            }
            var positionByParam = new Dictionary<string, string>();
            foreach (var pair in construction)
            {
                if (!pair.Value.IsForward)
                    continue;
                string paramId = pair.Value.genericParamId!;
                if (positionByParam.ContainsKey(paramId)
                    && scope.Find(param => param.id == paramId) is { } repeated)
                {
                    return new NeoInterfaceDetermination(NeoInterfaceDeterminationKind.RepeatedParam, repeated);
                }
                positionByParam[paramId] = pair.Key;
            }
            var paramByPosition = new Dictionary<string, string>(scope.Count);
            foreach (GenericParamDeclaration param in scope)
            {
                if (!positionByParam.TryGetValue(param.id, out string? positionId)
                    || !positionById.TryGetValue(positionId, out GenericParamDeclaration? position))
                {
                    return new NeoInterfaceDetermination(NeoInterfaceDeterminationKind.UnboundParam, param);
                }
                if (!ConstraintsEqual(param.constraint, position.constraint, positionByParam))
                {
                    return new NeoInterfaceDetermination(
                        NeoInterfaceDeterminationKind.NarrowedConstraint,
                        param,
                        position);
                }
                paramByPosition[positionId] = param.id;
            }
            return new NeoInterfaceDetermination(
                NeoInterfaceDeterminationKind.Determined,
                paramByPosition: paramByPosition);
        }

        /// <summary>
        /// The params left open at <paramref name="classId"/>, base first.
        /// Memoized along the extends chain so building every class's scope is
        /// linear: a class keeps its parent's scope, minus the parent's own
        /// params it binds, plus its own params.
        /// </summary>
        private List<GenericParamDeclaration> OpenScope(string classId)
        {
            if (scopeByClassId.TryGetValue(classId, out List<GenericParamDeclaration>? cached))
                return cached;
            if (!data.classes.TryGetValue(classId, out NeoSchemaClass? schemaClass))
                return EmptyScope;
            string key = "s:" + classId;
            if (!visiting.Add(key))
                return EmptyScope;
            string? parentId = schemaClass.extendsClassId;
            List<GenericParamDeclaration> parentScope = parentId is null ? EmptyScope : OpenScope(parentId);
            List<GenericParamDeclaration>? parentOwn = null;
            if (parentId is not null && data.classes.TryGetValue(parentId, out NeoSchemaClass? parent))
                parentOwn = parent.genericParams;
            var bindings = schemaClass.extendsGenericBindings;
            bool BoundHere(GenericParamDeclaration param) =>
                bindings?.ContainsKey(param.id) == true
                && parentOwn?.Exists(own => own.id == param.id) == true;
            List<GenericParamDeclaration> scope = parentScope;
            if (schemaClass.genericParams is { Count: > 0 } || parentScope.Exists(BoundHere))
            {
                scope = new List<GenericParamDeclaration>(parentScope.Count + (schemaClass.genericParams?.Count ?? 0));
                foreach (GenericParamDeclaration param in parentScope)
                {
                    if (!BoundHere(param))
                        scope.Add(param);
                }
                if (schemaClass.genericParams is { } own)
                    scope.AddRange(own);
            }
            visiting.Remove(key);
            scopeByClassId[classId] = scope;
            return scope;
        }

        private bool ConstraintsEqual(
            GenericParamConstraint? classConstraint,
            GenericParamConstraint? positionConstraint,
            IReadOnlyDictionary<string, string> positionByParam)
        {
            if (classConstraint is null)
                return positionConstraint is null;
            if (positionConstraint is null)
                return false;
            if (classConstraint.kind != positionConstraint.kind)
                return false;
            switch (classConstraint.kind)
            {
                case NeoGenericParamConstraintKind.Enum:
                    return classConstraint.enumId == positionConstraint.enumId;
                case NeoGenericParamConstraintKind.Interface:
                    return classConstraint.interfaceId == positionConstraint.interfaceId
                        && ArgumentsEqual(
                            classConstraint.interfaceArguments,
                            positionConstraint.interfaceArguments,
                            positionByParam);
                default:
                    return classConstraint.classId == positionConstraint.classId
                        && ArgumentsEqual(
                            classConstraint.classArguments,
                            positionConstraint.classArguments,
                            positionByParam);
            }
        }

        private bool ArgumentsEqual(
            IReadOnlyDictionary<string, GenericBinding>? classArguments,
            IReadOnlyDictionary<string, GenericBinding>? positionArguments,
            IReadOnlyDictionary<string, string> positionByParam)
        {
            classArguments ??= EmptyConstruction;
            positionArguments ??= EmptyConstruction;
            if (classArguments.Count != positionArguments.Count)
                return false;
            foreach (var pair in classArguments)
            {
                if (!positionArguments.TryGetValue(pair.Key, out GenericBinding? other))
                    return false;
                if (pair.Value.IsForward)
                {
                    if (!other.IsForward)
                        return false;
                    if (!positionByParam.TryGetValue(pair.Value.genericParamId!, out string? positionId)
                        || positionId != other.genericParamId)
                    {
                        return false;
                    }
                    continue;
                }
                if (other.IsForward)
                    return false;
                if (pair.Value.memberId == other.memberId)
                    continue;
                var a = NeoGenericResolution.SignatureOfBinding(client, pair.Value, NeoGenericResolution.EmptyEnv);
                var b = NeoGenericResolution.SignatureOfBinding(client, other, NeoGenericResolution.EmptyEnv);
                if (!NeoGenericResolution.SignaturesEqual(a, b))
                    return false;
            }
            return true;
        }

        private static IReadOnlyDictionary<string, GenericBinding> IdentityConstruction(
            List<GenericParamDeclaration>? genericParams)
        {
            if (genericParams is not { Count: > 0 })
                return EmptyConstruction;
            var construction = new Dictionary<string, GenericBinding>(genericParams.Count);
            foreach (GenericParamDeclaration param in genericParams)
            {
                construction[param.id] = new GenericBinding
                {
                    kind = NeoGenericBindingKind.Generic,
                    genericParamId = param.id,
                };
            }
            return construction;
        }

        /// <summary>
        /// Rewrites a construction's forwards through <paramref name="bindings"/>,
        /// which bind the params of the scope it was built in.
        /// </summary>
        private static IReadOnlyDictionary<string, GenericBinding> Substitute(
            IReadOnlyDictionary<string, GenericBinding> construction,
            IReadOnlyDictionary<string, GenericBinding> bindings)
        {
            Dictionary<string, GenericBinding>? result = null;
            foreach (var pair in construction)
            {
                if (!pair.Value.IsForward)
                    continue;
                if (!bindings.TryGetValue(pair.Value.genericParamId!, out GenericBinding? replacement))
                    continue;
                if (result is null)
                {
                    result = new Dictionary<string, GenericBinding>(construction.Count);
                    foreach (var copy in construction)
                        result[copy.Key] = copy.Value;
                }
                result[pair.Key] = replacement;
            }
            return result ?? construction;
        }

        private static bool ForwardsAnyBoundParam(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>> map,
            IReadOnlyDictionary<string, GenericBinding>? bindings)
        {
            if (bindings is null)
                return false;
            foreach (var construction in map.Values)
            {
                foreach (GenericBinding binding in construction.Values)
                {
                    if (binding.IsForward && bindings.ContainsKey(binding.genericParamId!))
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// P113 §6. The resolver generated code registers for one constructed
    /// interface. A static generic registry needs no runtime type
    /// construction, so it stays AOT-safe.
    /// </summary>
    public static class NeoInterfaceResolvers<I>
    {
        internal static NeoGeneratedTypesSupport.InterfaceResolver? Resolver
        {
            get; private set;
        }

        public static void Register(NeoGeneratedTypesSupport.InterfaceResolver resolver)
        {
            Resolver = resolver;
        }
    }
}
