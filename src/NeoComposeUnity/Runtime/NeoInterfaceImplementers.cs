// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
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

        /// <summary>
        /// Whether a row reads its params from the slot. A row of a class the
        /// interface leaves open reads them from its own stamp (#2556).
        /// </summary>
        internal bool Determines => kind is NeoInterfaceDeterminationKind.Closed
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
        private static readonly IReadOnlyDictionary<string, TypeBinding> EmptyConstruction =
            new Dictionary<string, TypeBinding>();

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>> EmptyMap =
            new Dictionary<string, IReadOnlyDictionary<string, TypeBinding>>();

        private static readonly IReadOnlyList<string> EmptyIds = new List<string>();

        private static readonly IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>> EmptyList =
            new List<IReadOnlyDictionary<string, TypeBinding>>();

        private static readonly List<GenericParamDeclaration> EmptyScope = new();

        private readonly NeoClient client;
        private readonly ProjectData data;
        private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>>>
            closureByInterfaceId = new();
        private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>>>
            mapByClassId = new();
        private readonly Dictionary<(string, string), IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>>>
            listByKey = new();
        private readonly Dictionary<(string, string), IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>>>
            interfaceListByKey = new();
        private readonly Dictionary<(string, string), NeoInterfaceDetermination> determinationByKey = new();
        private readonly Dictionary<string, List<GenericParamDeclaration>> scopeByClassId = new();
        private readonly Dictionary<(ClassMember, string), IReadOnlyDictionary<string, TypeBinding>?>
            slotArgumentsByKey = new();
        private readonly HashSet<string> visiting = new();
        private Dictionary<string, List<string>>? implementersByInterfaceId;

        internal NeoInterfaceImplementers(NeoClient client, ProjectData data)
        {
            this.client = client;
            this.data = data;
        }

        /// <summary>Every interface <paramref name="interfaceId"/> is or extends, with its arguments.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>> InterfaceClosure(
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
            var closure = new Dictionary<string, IReadOnlyDictionary<string, TypeBinding>>
            {
                [interfaceId] = IdentityConstruction(interfaceId, declaration.genericParams),
            };
            // P127 §4.7 lets a list hold several constructions of one
            // interface. This map keeps each interface's first construction;
            // ConstructionList and per-construction dispatch read them all.
            if (declaration.extendsInterfaces is not null)
            {
                foreach (InterfaceConstruction parent in declaration.extendsInterfaces)
                {
                    IReadOnlyDictionary<string, TypeBinding> parentArguments =
                        parent.arguments ?? EmptyConstruction;
                    foreach (var pair in InterfaceClosure(parent.interfaceId))
                        closure.TryAdd(pair.Key, Substitute(pair.Value, parentArguments));
                }
            }
            visiting.Remove(key);
            closureByInterfaceId[interfaceId] = closure;
            return closure;
        }

        /// <summary>Every interface <paramref name="classId"/> implements, with its arguments.</summary>
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>> Constructions(
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
            if (schemaClass.implementsInterfaces is { Count: > 0 } || ForwardsAnyBoundParam(parentMap, bindings))
            {
                var map = new Dictionary<string, IReadOnlyDictionary<string, TypeBinding>>(parentMap.Count);
                foreach (var pair in parentMap)
                    map[pair.Key] = bindings is null ? pair.Value : Substitute(pair.Value, bindings);
                if (schemaClass.implementsInterfaces is not null)
                {
                    foreach (InterfaceConstruction implemented in schemaClass.implementsInterfaces)
                    {
                        IReadOnlyDictionary<string, TypeBinding> implementedArguments =
                            implemented.arguments ?? EmptyConstruction;
                        foreach (var pair in InterfaceClosure(implemented.interfaceId))
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
            out IReadOnlyDictionary<string, TypeBinding> construction) =>
            Constructions(classId).TryGetValue(interfaceId, out construction!);

        /// <summary>
        /// Every construction of <paramref name="interfaceId"/>
        /// <paramref name="classId"/> implements: its base's, then its own
        /// base list's in order, each entry followed by the interfaces it
        /// extends (P127 §4.7). The server's <c>constructionList</c>.
        /// </summary>
        internal IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>> ConstructionList(
            string classId,
            string interfaceId)
        {
            var key = (classId, interfaceId);
            if (listByKey.TryGetValue(key, out var cached))
                return cached;
            if (!Constructions(classId).ContainsKey(interfaceId))
                return EmptyList;
            NeoSchemaClass schemaClass = data.classes[classId];
            string visitKey = "l:" + classId;
            if (!visiting.Add(visitKey))
                return EmptyList;
            var list = new List<IReadOnlyDictionary<string, TypeBinding>>();
            if (schemaClass.extendsClassId is { } baseId)
            {
                var bindings = schemaClass.extendsGenericBindings;
                foreach (var inherited in ConstructionList(baseId, interfaceId))
                    AddDistinct(list, bindings is null ? inherited : Substitute(inherited, bindings));
            }
            if (schemaClass.implementsInterfaces is not null)
            {
                foreach (InterfaceConstruction implemented in schemaClass.implementsInterfaces)
                {
                    foreach (var reached in InterfaceConstructionList(implemented.interfaceId, interfaceId))
                        AddDistinct(list, Substitute(reached, implemented.arguments ?? EmptyConstruction));
                }
            }
            visiting.Remove(visitKey);
            listByKey[key] = list;
            return list;
        }

        /// <summary>Every construction of <paramref name="interfaceId"/> that <paramref name="fromId"/> is or extends, in its own terms.</summary>
        private IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>> InterfaceConstructionList(
            string fromId,
            string interfaceId)
        {
            var key = (fromId, interfaceId);
            if (interfaceListByKey.TryGetValue(key, out var cached))
                return cached;
            if (!InterfaceClosure(fromId).ContainsKey(interfaceId))
                return EmptyList;
            Interface declaration = data.interfaces[fromId];
            string visitKey = "il:" + fromId;
            if (!visiting.Add(visitKey))
                return EmptyList;
            var list = new List<IReadOnlyDictionary<string, TypeBinding>>();
            if (fromId == interfaceId)
                list.Add(IdentityConstruction(interfaceId, declaration.genericParams));
            if (declaration.extendsInterfaces is not null)
            {
                foreach (InterfaceConstruction parent in declaration.extendsInterfaces)
                {
                    foreach (var reached in InterfaceConstructionList(parent.interfaceId, interfaceId))
                        AddDistinct(list, Substitute(reached, parent.arguments ?? EmptyConstruction));
                }
            }
            visiting.Remove(visitKey);
            interfaceListByKey[key] = list;
            return list;
        }

        private static void AddDistinct(
            List<IReadOnlyDictionary<string, TypeBinding>> list,
            IReadOnlyDictionary<string, TypeBinding> construction)
        {
            foreach (var known in list)
            {
                if (SameConstruction(known, construction))
                    return;
            }
            list.Add(construction);
        }

        private static bool SameConstruction(
            IReadOnlyDictionary<string, TypeBinding> a,
            IReadOnlyDictionary<string, TypeBinding> b)
        {
            if (a.Count != b.Count)
                return false;
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out TypeBinding? other) || !pair.Value.Equals(other))
                    return false;
            }
            return true;
        }

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
        /// Concrete classes that implement <paramref name="interfaceId"/>, in
        /// document order.
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
        internal IReadOnlyDictionary<string, TypeBinding>? SlotClassArguments(
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
            IReadOnlyDictionary<string, TypeBinding>? classArguments = null;
            if (Determination(rowClassId, interfaceId).paramByPosition is { } paramByPosition)
            {
                var byClassParam = new Dictionary<string, TypeBinding>(paramByPosition.Count);
                foreach (var pair in paramByPosition)
                {
                    if (slotArguments.TryGetValue(pair.Key, out TypeBinding? binding))
                        byClassParam[pair.Value] = binding;
                }
                classArguments = byClassParam;
            }
            slotArgumentsByKey[key] = classArguments;
            return classArguments;
        }

        /// <summary>
        /// Null when <paramref name="slotLabel"/> can hold the row
        /// <paramref name="rowId"/> of <paramref name="rowClassId"/>, else why
        /// not. The label is built only for a refusal.
        /// </summary>
        internal string? RowRefusal(System.Func<string> slotLabel, string rowId, MemberValue row, string rowClassId, string interfaceId)
        {
            if (!TryGetConstruction(rowClassId, interfaceId, out _))
                return NotImplementedRefusal(slotLabel(), rowClassId, interfaceId);
            return UnstampedRefusal(rowId, row, rowClassId, interfaceId);
        }

        /// <summary>
        /// A row of a class the interface leaves open reads its params from its
        /// own stamp, so it must store one (#2556).
        /// </summary>
        internal string? UnstampedRefusal(string rowId, MemberValue row, string rowClassId, string interfaceId)
        {
            if (row.genericBindings is not null)
                return null;
            NeoInterfaceDetermination determination = Determination(rowClassId, interfaceId);
            if (determination.kind is NeoInterfaceDeterminationKind.NotImplemented || determination.Determines)
                return null;
            return $"Value \"{rowId}\" is a binding node and stores no stamp.";
        }

        private string NotImplementedRefusal(string slotLabel, string rowClassId, string interfaceId)
        {
            string rowName = ClassName(rowClassId);
            return $"{slotLabel} can't hold a {rowName}, because {NotImplementedClause(rowName, interfaceId)}.";
        }

        private string NotImplementedClause(string rowName, string interfaceId) =>
            $"{rowName} doesn't implement {InterfaceName(interfaceId)}";

        /// <summary>
        /// P113 §3.2 rule 1. Null when a row of <paramref name="rowClassId"/>
        /// fits <paramref name="slot"/>: one construction of the slot's
        /// interface its class implements converts to the slot's (P127 §4.7,
        /// §4.8). An
        /// argument the row's class fills with its own param comes from the
        /// slot, and one <paramref name="contextEnv"/> leaves open at any depth
        /// fits every row. Mirrors the server's <c>interfaceSlotRefusal</c>.
        /// The slot's label, <paramref name="ownerName"/>.<paramref name="key"/>
        /// or a bare <paramref name="key"/>, is built only for a refusal.
        /// </summary>
        internal string? SlotRefusal(
            ClassMember slot,
            string? ownerName,
            string key,
            string rowClassId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> contextEnv)
        {
            string interfaceId = slot.DeclaredInterfaceId!;
            IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>> constructions = ConstructionList(rowClassId, interfaceId);
            if (constructions.Count == 0)
                return NotImplementedRefusal(SlotLabel(ownerName, key), rowClassId, interfaceId);
            if (slot.interfaceArguments is not { Count: > 0 } slotArguments)
                return null;
            foreach (var construction in constructions)
            {
                if (ConstructionFitsSlot(interfaceId, construction, slotArguments, contextEnv))
                    return null;
            }
            string rowName = ClassName(rowClassId);
            var labels = new List<string>(constructions.Count);
            foreach (var construction in constructions)
                labels.Add(ConstructionLabel(interfaceId, construction, NeoGenericResolution.EmptyEnv));
            string wanted = ConstructionLabel(interfaceId, slotArguments, contextEnv);
            return $"{SlotLabel(ownerName, key)} can't hold a {rowName}, because {rowName} implements {ListLabel(labels)}, not {wanted}.";
        }

        /// <summary>
        /// Each position <paramref name="construction"/> closes converts to
        /// the slot's argument, by variance where the interface declares it
        /// (P127 §4.8). A position the row's class fills with its own param,
        /// or <paramref name="contextEnv"/> leaves open, fits.
        /// </summary>
        private bool ConstructionFitsSlot(
            string interfaceId,
            IReadOnlyDictionary<string, TypeBinding> construction,
            IReadOnlyDictionary<string, TypeBinding> slotArguments,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> contextEnv)
        {
            foreach (var pair in slotArguments)
            {
                if (!construction.TryGetValue(pair.Key, out TypeBinding? rowBinding))
                    continue;
                if (rowBinding.IsOpen)
                    continue;
                if (client.TypeRules.BindingText(pair.Value, contextEnv) is null)
                    continue;
                var slotSignature = NeoGenericResolution.SignatureOfBinding(client, pair.Value, contextEnv);
                var rowSignature = NeoGenericResolution.SignatureOfBinding(client, rowBinding, NeoGenericResolution.EmptyEnv);
                if (NeoGenericResolution.SignaturesEqual(slotSignature, rowSignature))
                    continue;
                GenericParamDeclaration? parameter = InterfaceParam(interfaceId, pair.Key);
                if (parameter?.variance is null)
                    return false;
                if (!client.TypeRules.ArgumentConverts(
                        client,
                        parameter,
                        NeoGenericResolution.TypeInfoFromArgumentSignature(rowSignature),
                        NeoGenericResolution.TypeInfoFromArgumentSignature(slotSignature)))
                {
                    return false;
                }
            }
            return true;
        }

        private GenericParamDeclaration? InterfaceParam(string interfaceId, string paramId)
        {
            if (!data.interfaces.TryGetValue(interfaceId, out Interface? declaration))
                return null;
            return declaration.genericParams?.Find(parameter => parameter.id == paramId);
        }

        /// <summary><c>A</c>, <c>A and B</c>, <c>A, B and C</c>.</summary>
        private static string ListLabel(List<string> labels)
        {
            if (labels.Count < 2)
                return string.Concat(labels);
            return $"{string.Join(", ", labels.GetRange(0, labels.Count - 1))} and {labels[labels.Count - 1]}";
        }

        /// <summary>
        /// P113 §3.6. Why <paramref name="label"/>, a reference typed
        /// <paramref name="target"/>, can't reference <paramref name="targetId"/>,
        /// a row of <paramref name="rowClassId"/>; null when it can. Positions
        /// the row's class fills with its own params read
        /// <paramref name="rowArguments"/>, and are skipped without them. The
        /// server's Lookup refusal, word for word.
        /// </summary>
        internal string? TargetRefusal(
            string label,
            string targetId,
            string rowClassId,
            InterfaceTypeInfo target,
            IReadOnlyDictionary<string, TypeInfo>? rowArguments)
        {
            if (TargetMismatch(target, rowClassId, rowArguments) is not { } mismatch)
                return null;
            string refusal = $"{label} can't reference '{targetId}', because {mismatch}.";
            NeoInterfaceDeterminationKind kind = Determination(rowClassId, target.interfaceId).kind;
            if (kind is NeoInterfaceDeterminationKind.Closed
                or NeoInterfaceDeterminationKind.Determined
                or NeoInterfaceDeterminationKind.NotImplemented)
            {
                return refusal;
            }
            return ClosedImplementerExample(rowClassId, target) is { } example
                ? $"{refusal} Reference a class that binds it, such as {example}."
                : refusal;
        }

        /// <summary>
        /// Null when a row of <paramref name="rowClassId"/> fits a reference
        /// typed <paramref name="target"/>, else the clause saying why not.
        /// The server's <c>interfaceTargetMismatch</c>.
        /// </summary>
        internal string? TargetMismatch(
            InterfaceTypeInfo target,
            string rowClassId,
            IReadOnlyDictionary<string, TypeInfo>? rowArguments)
        {
            IReadOnlyList<IReadOnlyDictionary<string, TypeBinding>> constructions = ConstructionList(rowClassId, target.interfaceId);
            if (constructions.Count == 0)
                return NotImplementedClause(ClassName(rowClassId), target.interfaceId);
            foreach (var construction in constructions)
            {
                if (ConstructionMatches(construction, rowClassId, target, rowArguments))
                    return null;
            }
            var labels = new List<string>(constructions.Count);
            foreach (var construction in constructions)
            {
                labels.Add(rowArguments is null
                    ? ConstructionLabel(target.interfaceId, construction, NeoGenericResolution.EmptyEnv)
                    : NeoReferenceTypes.Format(client, new InterfaceTypeInfo
                    {
                        type = MemberKind.Interface,
                        required = true,
                        interfaceId = target.interfaceId,
                        typeArguments = ConstructionTypeArguments(construction, rowClassId, rowArguments),
                    }));
            }
            string wanted = NeoReferenceTypes.Format(client, new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = target.interfaceId,
                typeArguments = target.typeArguments,
            });
            return $"{ClassName(rowClassId)} implements {ListLabel(labels)}, not {wanted}";
        }

        /// <summary><paramref name="construction"/>'s positions convert to <paramref name="target"/>'s arguments, by variance where the interface declares it (P127 §4.8); one naming the row's params without <paramref name="rowArguments"/> is skipped. A param nothing closes is its own argument, equal only to itself.</summary>
        private bool ConstructionMatches(
            IReadOnlyDictionary<string, TypeBinding> construction,
            string rowClassId,
            InterfaceTypeInfo target,
            IReadOnlyDictionary<string, TypeInfo>? rowArguments)
        {
            if (target.typeArguments is not { } expected)
                return true;
            foreach (KeyValuePair<string, TypeBinding> position in construction)
            {
                if (!expected.TryGetValue(position.Key, out TypeInfo? argument))
                    return false;
                if (ConstructionArgument(position.Value, rowClassId, rowArguments) is not { } actual)
                {
                    if (!NeoTypeSources.IsFullyClosed(position.Value))
                        continue;
                    return false;
                }
                if (!client.TypeRules.ArgumentConverts(client, InterfaceParam(target.interfaceId, position.Key), actual, argument))
                    return false;
            }
            return true;
        }

        private Dictionary<string, TypeInfo> ConstructionTypeArguments(
            IReadOnlyDictionary<string, TypeBinding> construction,
            string rowClassId,
            IReadOnlyDictionary<string, TypeInfo> rowArguments)
        {
            var typeArguments = new Dictionary<string, TypeInfo>(construction.Count);
            foreach (KeyValuePair<string, TypeBinding> position in construction)
            {
                if (ConstructionArgument(position.Value, rowClassId, rowArguments) is { } actual)
                    typeArguments[position.Key] = actual;
            }
            return typeArguments;
        }

        /// <summary>The type a construction position spells once <paramref name="rowArguments"/> closes the row's params it names, at any depth; null without them. The server's <c>boundTypeArgumentTypeInfo</c>.</summary>
        private TypeInfo? ConstructionArgument(TypeBinding binding, string rowClassId, IReadOnlyDictionary<string, TypeInfo>? rowArguments)
        {
            if (binding.IsOpen)
                return rowArguments is not null && rowArguments.TryGetValue(binding.Param!, out TypeInfo? argument) ? argument : null;
            if (NeoTypeBindingViews.TryView(binding, out _) is not Member member)
                return null;
            TypeInfo typeInfo = NeoReferenceTypes.SlotTypeInfo(client, member);
            if (NeoTypeSources.IsFullyClosed(binding))
                return typeInfo;
            // ListBox<T> : Box<List<T>> names its param inside the position.
            return rowArguments is null ? null : NeoTypeRules.SubstituteParams(typeInfo, rowClassId, rowArguments);
        }

        /// <summary>
        /// A concrete class the interface closes, under <paramref name="rowClassId"/>,
        /// that a reference typed <paramref name="target"/> accepts: the
        /// "such as" in a refusal. Runs only on an error path.
        /// </summary>
        private string? ClosedImplementerExample(string rowClassId, InterfaceTypeInfo target)
        {
            foreach (string classId in Implementers(target.interfaceId))
            {
                if (Determination(classId, target.interfaceId).kind != NeoInterfaceDeterminationKind.Closed)
                    continue;
                if (!client.ClassChainContains(classId, rowClassId))
                    continue;
                foreach (var construction in ConstructionList(classId, target.interfaceId))
                {
                    if (ConstructionMatches(construction, classId, target, null))
                        return ClassName(classId);
                }
            }
            return null;
        }

        private static string SlotLabel(string? ownerName, string key) =>
            ownerName is null ? key : $"{ownerName}.{key}";

        /// <summary><c>IContainer&lt;Seed&gt;</c> for a construction under <paramref name="env"/>.</summary>
        private string ConstructionLabel(
            string interfaceId,
            IReadOnlyDictionary<string, TypeBinding> construction,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            string name = InterfaceName(interfaceId);
            if (!data.interfaces.TryGetValue(interfaceId, out Interface? declaration)
                || declaration.genericParams is not { Count: > 0 } parameters)
            {
                return name;
            }
            var labels = new string[parameters.Count];
            for (int index = 0; index < parameters.Count; index++)
            {
                GenericParamDeclaration parameter = parameters[index];
                if (!construction.TryGetValue(parameter.id, out TypeBinding? binding))
                {
                    labels[index] = parameter.name;
                    continue;
                }
                if (!binding.IsOpen)
                {
                    labels[index] = client.TypeRules.BindingText(binding, env) ?? NeoTypeNames.Of(binding, data);
                    continue;
                }
                labels[index] = env.TryGetValue(binding.Param!, out NeoGenericEnvEntry? entry) && entry.IsBound
                    ? EntryLabel(entry, env)
                    : ParamName(binding.Param!);
            }
            return $"{name}<{string.Join(", ", labels)}>";
        }

        private string EntryLabel(NeoGenericEnvEntry entry, IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            TypeBinding binding = NeoTypeBindings.ArgumentOfEntry(client, entry, env);
            return client.TypeRules.BindingText(binding, env) ?? NeoTypeNames.Of(binding, data);
        }

        /// <summary>A class or interface param's name. Runs only on an error path.</summary>
        private string ParamName(string paramId)
        {
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                if (schemaClass.genericParams?.Find(param => param.id == paramId) is { } classParam)
                    return classParam.name;
            }
            foreach (Interface declaration in data.interfaces.Values)
            {
                if (declaration.genericParams?.Find(param => param.id == paramId) is { } interfaceParam)
                    return interfaceParam.name;
            }
            return paramId;
        }

        internal string InterfaceName(string interfaceId) =>
            data.interfaces.TryGetValue(interfaceId, out Interface? declaration)
                ? declaration.name
                : interfaceId;

        private string ClassName(string classId) =>
            data.classes.TryGetValue(classId, out NeoSchemaClass? schemaClass)
                ? schemaClass.name
                : classId;

        private Dictionary<string, List<string>> BuildImplementers()
        {
            var byInterface = new Dictionary<string, List<string>>();
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                if (schemaClass.IsAbstract)
                    continue;
                foreach (string interfaceId in Constructions(schemaClass.id).Keys)
                {
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
                if (!pair.Value.IsOpen)
                    continue;
                string paramId = pair.Value.Param!;
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
                if (!ConstraintListsEqual(param.constraints, position.constraints, positionByParam))
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

        /// <summary>
        /// P113 §1: a class param's constraints equal the interface position's,
        /// once each class param is read as the position it fills. Order
        /// doesn't matter (P127 §4.7: the closure is their union).
        /// </summary>
        private static readonly List<GenericParamConstraint> EmptyConstraints = new List<GenericParamConstraint>();

        private bool ConstraintListsEqual(
            List<GenericParamConstraint>? classConstraints,
            List<GenericParamConstraint>? positionConstraints,
            IReadOnlyDictionary<string, string> positionByParam)
        {
            int count = classConstraints?.Count ?? 0;
            if (count != (positionConstraints?.Count ?? 0))
                return false;
            foreach (GenericParamConstraint classConstraint in classConstraints ?? EmptyConstraints)
            {
                bool matched = false;
                foreach (GenericParamConstraint position in positionConstraints!)
                {
                    if (ConstraintsEqual(classConstraint, position, positionByParam))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                    return false;
            }
            return true;
        }

        private bool ConstraintsEqual(
            GenericParamConstraint classConstraint,
            GenericParamConstraint positionConstraint,
            IReadOnlyDictionary<string, string> positionByParam)
        {
            if (classConstraint.kind != positionConstraint.kind)
                return false;
            if (classConstraint.nullable != positionConstraint.nullable)
                return false;
            switch (classConstraint.kind)
            {
                case NeoGenericParamConstraintKind.AnyEnum:
                    return true;
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
            IReadOnlyDictionary<string, TypeBinding>? classArguments,
            IReadOnlyDictionary<string, TypeBinding>? positionArguments,
            IReadOnlyDictionary<string, string> positionByParam)
        {
            classArguments ??= EmptyConstruction;
            positionArguments ??= EmptyConstruction;
            if (classArguments.Count != positionArguments.Count)
                return false;
            foreach (var pair in classArguments)
            {
                if (!positionArguments.TryGetValue(pair.Key, out TypeBinding? other))
                    return false;
                if (pair.Value.IsOpen)
                {
                    if (!other.IsOpen)
                        return false;
                    if (!positionByParam.TryGetValue(pair.Value.Param!, out string? positionId)
                        || positionId != other.Param)
                    {
                        return false;
                    }
                    continue;
                }
                if (other.IsOpen)
                    return false;
                if (pair.Value.Equals(other))
                    continue;
                var a = NeoGenericResolution.SignatureOfBinding(client, pair.Value, NeoGenericResolution.EmptyEnv);
                var b = NeoGenericResolution.SignatureOfBinding(client, other, NeoGenericResolution.EmptyEnv);
                if (!NeoGenericResolution.SignaturesEqual(a, b))
                    return false;
            }
            return true;
        }

        private static IReadOnlyDictionary<string, TypeBinding> IdentityConstruction(
            string interfaceId,
            List<GenericParamDeclaration>? genericParams)
        {
            if (genericParams is not { Count: > 0 })
                return EmptyConstruction;
            var owner = new TypeDefinition(MemberKind.Interface, interfaceId: interfaceId);
            var construction = new Dictionary<string, TypeBinding>(genericParams.Count);
            foreach (GenericParamDeclaration param in genericParams)
                construction[param.id] = TypeBinding.Open(owner, param.id);
            return construction;
        }

        /// <summary>
        /// Rewrites a construction's forwards through <paramref name="bindings"/>,
        /// which bind the params of the scope it was built in.
        /// </summary>
        private static IReadOnlyDictionary<string, TypeBinding> Substitute(
            IReadOnlyDictionary<string, TypeBinding> construction,
            IReadOnlyDictionary<string, TypeBinding> bindings)
        {
            Dictionary<string, TypeBinding>? result = null;
            foreach (var pair in construction)
            {
                if (!pair.Value.IsOpen)
                    continue;
                if (!bindings.TryGetValue(pair.Value.Param!, out TypeBinding? replacement))
                    continue;
                if (result is null)
                {
                    result = new Dictionary<string, TypeBinding>(construction.Count);
                    foreach (var copy in construction)
                        result[copy.Key] = copy.Value;
                }
                result[pair.Key] = replacement;
            }
            return result ?? construction;
        }

        private static bool ForwardsAnyBoundParam(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeBinding>> map,
            IReadOnlyDictionary<string, TypeBinding>? bindings)
        {
            if (bindings is null)
                return false;
            foreach (var construction in map.Values)
            {
                foreach (TypeBinding binding in construction.Values)
                {
                    if (binding.IsOpen && bindings.ContainsKey(binding.Param!))
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
