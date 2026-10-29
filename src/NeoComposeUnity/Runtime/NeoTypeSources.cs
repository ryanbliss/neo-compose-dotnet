// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>The row a <see cref="NeoTypeSource"/> points at: a binding node of a class, or a box row.</summary>
    internal sealed class NeoTypeSourceOrigin
    {
        internal NeoTypeSourceOrigin(string valueId, string? classId)
        {
            ValueId = valueId;
            ClassId = classId;
        }

        internal string ValueId
        {
            get;
        }

        /// <summary>The binding node's class; null for a box row.</summary>
        internal string? ClassId
        {
            get;
        }
    }

    /// <summary>
    /// One placed position: the row, the member it sits under (a schema
    /// member or a binding view; null for an ownerless row), and its type
    /// twice, read against its source and closed. <see cref="Declared"/>'s
    /// open arms mark the params of its declaring scope.
    /// </summary>
    internal sealed class NeoTypeSourcePosition
    {
        internal NeoTypeSourcePosition(
            string valueId,
            Member? placement,
            TypeBinding sourced,
            TypeBinding concrete,
            NeoTypeSourceOrigin? origin,
            TypeBinding declared,
            string? templateClassId)
        {
            ValueId = valueId;
            Placement = placement;
            Sourced = sourced;
            Concrete = concrete;
            Origin = origin;
            Declared = declared;
            TemplateClassId = templateClassId;
        }

        internal string ValueId
        {
            get;
        }

        internal Member? Placement
        {
            get;
        }

        internal TypeBinding Sourced
        {
            get;
        }

        internal TypeBinding Concrete
        {
            get;
        }

        /// <summary>The nearest owning binding node or box row, or null.</summary>
        internal NeoTypeSourceOrigin? Origin
        {
            get;
        }

        internal TypeBinding Declared
        {
            get;
        }

        /// <summary>
        /// Under a member default that uses its owning class's params (§12.1),
        /// that class: its params stay open in each type, with no row to name.
        /// </summary>
        internal string? TemplateClassId
        {
            get;
        }
    }

    /// <summary>What P127 §12.1 requires of one placed row.</summary>
    internal sealed class NeoTypeSourceRequirement
    {
        internal NeoTypeSourceRequirement(
            MemberValueBase row,
            NeoTypeSourcePosition position,
            string[]? stampParams,
            Dictionary<string, TypeBinding>? spelledStamp,
            NeoTypeSource? typeSource,
            string? bindingClassId)
        {
            Row = row;
            Position = position;
            StampParams = stampParams;
            SpelledStamp = spelledStamp;
            TypeSource = typeSource;
            BindingClassId = bindingClassId;
        }

        internal MemberValueBase Row
        {
            get;
        }

        internal string RowId => Position.ValueId;

        internal NeoTypeSourcePosition Position
        {
            get;
        }

        /// <summary>A binding node's stamped params; null where the row may store no stamp, empty for a class that closes nothing.</summary>
        internal string[]? StampParams
        {
            get;
        }

        /// <summary>The stamp the position spells, for a binding node whose type does.</summary>
        internal Dictionary<string, TypeBinding>? SpelledStamp
        {
            get;
        }

        /// <summary>The typeSource the row must store; null where it stores none.</summary>
        internal NeoTypeSource? TypeSource
        {
            get;
        }

        /// <summary>The class a binding node stores; null elsewhere.</summary>
        internal string? BindingClassId
        {
            get;
        }
    }

    /// <summary>The rows one walk reads. A row it doesn't hold is skipped with its subtree.</summary>
    internal interface INeoTypeSourceGraph
    {
        MemberValueBase? Row(string valueId);

        /// <summary>An unordered list's entry ids, by <c>containerId</c>.</summary>
        IEnumerable<string> UnorderedEntryIds(string containerId);
    }

    /// <summary>
    /// P127 §12.1, the compose <c>type-source-walk.ts</c> and
    /// <c>type-sources.ts</c>. One walk places each row: a
    /// <c>genericBindings</c> stamp only on a binding node, which always
    /// stores its class; a <see cref="NeoTypeSource"/> on each open container
    /// and each class row in a declared generic slot; nothing on a scalar.
    /// Writers apply it where they create rows (<see cref="PlaceCreatedUnder"/>, <see cref="PlaceConstructed"/>) and
    /// validators compare against it (<see cref="Violation"/>). Readers
    /// resolve a placed row in at most one hop and never climb its owners
    /// (<see cref="PlacedRowType"/>, <see cref="PlacedRowStamp"/>).
    /// </summary>
    internal static class NeoTypeSources
    {
        private const string SourceOwnerClassId = "$type-source";
        private const string DeclaredOwnerClassId = "$declared";
        private const string DependentParam = "*";
        private const string AnimationChildOverridesMemberId = "system_a5905750-c472-46a8-89e9-f04f0bf66696";
        private const string AnimationChildOverridesEntryMemberId = "system_86688965-6b26-529e-95f3-a4070f022582";

        private static readonly TypeDefinition SourceOwner = new(MemberKind.Class, classId: SourceOwnerClassId);
        private static readonly TypeDefinition DeclaredOwner = new(MemberKind.Class, classId: DeclaredOwnerClassId);
        private static readonly TypeBinding SourceDependent = TypeBinding.Open(SourceOwner, DependentParam);
        private static readonly TypeBinding ClosedUnknown = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
        private static readonly Dictionary<string, TypeBinding> NoEnv = new(StringComparer.Ordinal);

        // ------------------------------------------------------------------
        // Positions.
        // ------------------------------------------------------------------

        /// <summary>
        /// The position of a root placed under <paramref name="placement"/>.
        /// A static root reads the params its declaring class closes through
        /// its chain's <c>extends</c> bindings. Null where no type spells the
        /// member, so nothing under it is typed by a param.
        /// </summary>
        internal static NeoTypeSourcePosition? RootPosition(
            NeoClient client,
            string valueId,
            Member placement,
            string? declaringClassId = null,
            Func<string, TypeBinding?>? ownArgument = null)
        {
            if (!HasTypeBinding(client, placement))
                return null;
            Dictionary<string, TypeBinding> env = declaringClassId is null
                ? NoEnv
                : ClassSourcedEnv(client, declaringClassId, ownArgument ?? (_ => null));
            TypeBinding type = SourcedMemberType(client, placement, env);
            return new NeoTypeSourcePosition(valueId, placement, type, type, null, DeclaredMemberType(client, placement), null);
        }

        /// <summary>The position of an ownerless row: always a binding node.</summary>
        internal static NeoTypeSourcePosition OwnerlessPosition(string valueId) =>
            new(valueId, null, ClosedUnknown, ClosedUnknown, null, ClosedUnknown, null);

        /// <summary>The position of a root whose record spells its closed class type, such as a variant root: never a binding node.</summary>
        internal static NeoTypeSourcePosition TypedRootPosition(string valueId, TypeBinding type) =>
            new(valueId, NeoTypeBindingViews.View(type), type, type, null, type, null);

        /// <summary>
        /// The position of a member default's inline root (§12.1), which has
        /// no row id: <paramref name="valueId"/> is the id its graph holds it
        /// under. A default that uses <paramref name="owningClassId"/>'s
        /// params is a template: its descendants store their type open, with
        /// no row to name. Null where no type spells the member.
        /// </summary>
        internal static NeoTypeSourcePosition? DefaultRootPosition(
            NeoClient client,
            string valueId,
            Member placement,
            string? owningClassId)
        {
            if (!HasTypeBinding(client, placement))
                return null;
            Dictionary<string, TypeBinding> env = owningClassId is null
                ? NoEnv
                : ClassSourcedEnv(
                    client,
                    owningClassId,
                    paramId => TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: owningClassId), paramId));
            TypeBinding type = SourcedMemberType(client, placement, env);
            return new NeoTypeSourcePosition(
                valueId,
                placement,
                type,
                type,
                null,
                DeclaredMemberType(client, placement),
                owningClassId);
        }

        /// <summary>
        /// The typeSource <paramref name="member"/>'s inline default root
        /// stores once read as row <paramref name="root"/> (§12.1): open over
        /// its owning class's params, with no valueId, for a template default;
        /// closed for a closed one. Its stored descendants carry their own
        /// from the export. Depends only on the schema, so it's memoized per
        /// member until the client's schema resolutions reset.
        /// </summary>
        internal static NeoTypeSource? DefaultRootTypeSource(NeoClient client, Member member, MemberValue root)
        {
            object resolution = client.SchemaResolution;
            if (ReferenceEquals(Volatile.Read(ref member.defaultRootTypeSourceResolution), resolution))
                return member.defaultRootTypeSource;
            NeoTypeSource? typeSource = null;
            if (root is ObjectMemberValue or ArrayMemberValue)
            {
                Member placement = SchemaMember(client, member);
                var graph = new RowsGraph(id => id == root.id ? root : null, _ => Array.Empty<string>());
                Walk(
                    client,
                    graph,
                    DefaultRootPosition(client, root.id, placement, client.SchemaOwnerClassId(placement.id)),
                    placed =>
                    {
                        if (placed.RowId == root.id)
                            typeSource = placed.TypeSource;
                    });
            }
            member.defaultRootTypeSource = typeSource;
            Volatile.Write(ref member.defaultRootTypeSourceResolution, resolution);
            return typeSource;
        }

        // ------------------------------------------------------------------
        // The walk.
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the rows under <paramref name="position"/>, parent first,
        /// and hands each placed row what the placement rule requires of it.
        /// </summary>
        internal static void Walk(
            NeoClient client,
            INeoTypeSourceGraph graph,
            NeoTypeSourcePosition? position,
            Action<NeoTypeSourceRequirement> visit)
        {
            if (position is null)
                return;
            Walk(client, graph, new[] { position }, visit);
        }

        private static void Walk(
            NeoClient client, INeoTypeSourceGraph graph, IEnumerable<NeoTypeSourcePosition> positions,
            Action<NeoTypeSourceRequirement> visit)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<NeoTypeSourcePosition>(positions);
            while (pending.Count > 0)
            {
                NeoTypeSourcePosition next = pending.Pop();
                if (!seen.Add(next.ValueId))
                    continue;
                if (graph.Row(next.ValueId) is not MemberValueBase row)
                    continue;
                Place(client, row, next, visit, pending.Push, graph.UnorderedEntryIds, graph.Row, true);
            }
        }

        /// <summary>Writes the placement rule onto the rows under <paramref name="position"/>.</summary>
        internal static void Place(NeoClient client, INeoTypeSourceGraph graph, NeoTypeSourcePosition? position) =>
            Walk(client, graph, position, Apply);

        /// <summary>The placement rule's refusals for the rows under <paramref name="position"/> (§12.2), one message each.</summary>
        internal static List<string> Violations(NeoClient client, INeoTypeSourceGraph graph, NeoTypeSourcePosition? position)
        {
            var violations = new List<string>();
            Walk(client, graph, position, placed =>
            {
                if (Violation(placed) is string violation)
                    violations.Add(violation);
            });
            return violations;
        }

        /// <summary>
        /// Places one row and hands each child it reaches to
        /// <paramref name="push"/>. <paramref name="wants"/> filters children
        /// before their position is built, so a caller following one path
        /// allocates only that child's. A class row's constructor arguments
        /// are children only when <paramref name="constructorArguments"/> is set.
        /// </summary>
        private static void Place(
            NeoClient client,
            MemberValueBase row,
            NeoTypeSourcePosition position,
            Action<NeoTypeSourceRequirement>? visit,
            Action<NeoTypeSourcePosition> push,
            Func<string, IEnumerable<string>> unorderedEntryIds,
            Func<string, MemberValueBase?> wants,
            bool constructorArguments)
        {
            Member? declared = position.Placement;
            bool bindingPlacement = IsBindingNodePlacement(client, declared);
            bool structured = row is ObjectMemberValue or ArrayMemberValue or ObjectMemberValueBase or ArrayMemberValueBase;
            if (structured && ContainerValue(row) is null)
            {
                visit?.Invoke(new NeoTypeSourceRequirement(row, position, null, null, null, null));
                return;
            }
            if (row.valueType is not null)
            {
                PlaceBoxRow(client, row, position, visit, push, unorderedEntryIds, wants);
                return;
            }
            if (!structured)
            {
                visit?.Invoke(new NeoTypeSourceRequirement(row, position, null, null, null, null));
                return;
            }
            // A class row names its class, even in a template's open slot.
            if (row.classId is string rowClassId)
            {
                PlaceClassRow(client, row, rowClassId, position, bindingPlacement, visit, push, wants, constructorArguments);
                return;
            }
            // A type-parameter slot reads through its bound type's view, whose
            // class a row of that class omits.
            Member member = TypedMember(declared, position.Concrete);
            if (IsClassSlot(member))
            {
                PlaceClassRow(client, row, SlotClassId(member), position, bindingPlacement, visit, push, wants, constructorArguments);
                return;
            }
            bool container = member is ListMember or DictionaryMember or TupleMember;
            visit?.Invoke(new NeoTypeSourceRequirement(
                row,
                position,
                null,
                null,
                container && (HasOpenArm(position.Declared) || NamesSourceBinding(position.Sourced, position.TemplateClassId))
                    ? TypeSourceOf(position.Origin, position.Sourced, position.Concrete, position.TemplateClassId)
                    : null,
                null));
            if (container)
                PushEntries(client, row, member, position, push, unorderedEntryIds, wants);
        }

        /// <summary>A kind whose value is a row of its own (§12.1).</summary>
        internal static bool ConstructorArgumentStoresRow(MemberKind kind) =>
            kind is MemberKind.Class or MemberKind.Interface or MemberKind.Object or MemberKind.List
                or MemberKind.Set or MemberKind.Dictionary or MemberKind.Tuple;

        internal static bool ConstructorArgumentStoresRow(TypeInfo type) =>
            ConstructorArgumentStoresRow(type.type)
            || NeoTypeRules.Plain(type) is LookupTypeInfo { collectionMemberId: null };

        private static object? ContainerValue(MemberValueBase row) => row switch
        {
            ObjectMemberValue objectRow => objectRow.value,
            ArrayMemberValue arrayRow => arrayRow.value,
            ObjectMemberValueBase objectBase => objectBase.value,
            ArrayMemberValueBase arrayBase => arrayBase.value,
            _ => null,
        };

        private static bool IsClassSlot(Member member) =>
            member is ClassMember classMember && classMember.DeclaredClassId is not null;

        private static string SlotClassId(Member member) =>
            (member as ClassMember)?.DeclaredClassId
                ?? throw new InvalidOperationException("A class row needs a class slot or its own classId.");

        /// <summary>
        /// The member a row is read through: its schema member, or the
        /// binding view of its closed type where the schema member is a type
        /// parameter.
        /// </summary>
        private static Member TypedMember(Member? declared, TypeBinding concrete)
        {
            if (declared is not null and not GenericMember)
                return declared;
            if (!IsFullyClosed(concrete))
                throw new InvalidOperationException($"A placed row's type is open where its placement closes it ({concrete.Kind}).");
            return NeoTypeBindingViews.View(concrete);
        }

        private static void PlaceBoxRow(
            NeoClient client,
            MemberValueBase row,
            NeoTypeSourcePosition position,
            Action<NeoTypeSourceRequirement>? visit,
            Action<NeoTypeSourcePosition> push,
            Func<string, IEnumerable<string>> unorderedEntryIds,
            Func<string, MemberValueBase?> wants)
        {
            TypeBinding valueType = row.valueType!;
            visit?.Invoke(new NeoTypeSourceRequirement(row, position, null, null, null, null));
            Member? payload = NeoTypeBindingViews.PayloadView(valueType, out _);
            if (payload is not (ListMember or DictionaryMember or TupleMember))
                return;
            PushEntries(
                client,
                row,
                payload,
                new NeoTypeSourcePosition(
                    position.ValueId,
                    payload,
                    SourcePathType(Array.Empty<string>()),
                    valueType,
                    new NeoTypeSourceOrigin(position.ValueId, null),
                    valueType,
                    null),
                push,
                unorderedEntryIds,
                wants);
        }

        private static void PlaceClassRow(
            NeoClient client,
            MemberValueBase row,
            string classId,
            NeoTypeSourcePosition position,
            bool bindingPlacement,
            Action<NeoTypeSourceRequirement>? visit,
            Action<NeoTypeSourcePosition> push,
            Func<string, MemberValueBase?> wants,
            bool constructorArguments,
            bool fields = true)
        {
            IList<NeoSchemaClass> chain = client.ResolveClassInheritanceChain(classId);
            string[] ownParams = chain.Count == 0 || chain[0].genericParams is null
                ? Array.Empty<string>()
                : chain[0].genericParams!.Select(param => param.id).ToArray();
            Dictionary<string, TypeBinding>? slotEnv = bindingPlacement
                ? null
                : ConcreteClassEnv(client, classId, position.Concrete);
            // A slot that can't spell the row's own arguments, such as a
            // generic subclass in a base-class slot, makes the row a binding
            // node too.
            bool bindingNode = slotEnv is null || ownParams.Any(paramId => !slotEnv.ContainsKey(paramId));
            Dictionary<string, TypeBinding> concreteEnv;
            Dictionary<string, TypeBinding> sourcedEnv;
            NeoTypeSourceOrigin? origin = position.Origin;
            Dictionary<string, TypeBinding>? spelled = null;
            Dictionary<string, TypeBinding>? stored = (row as MemberValue)?.genericBindings;
            if (bindingNode || slotEnv is null)
            {
                spelled = SpelledStamp(position.Concrete, classId, ownParams)
                    ?? (stored is null ? DeterminedStamp(client, classId, ownParams, position.Concrete) : null);
                Dictionary<string, TypeBinding> stamp = stored ?? spelled ?? NoEnv;
                concreteEnv = ClassSourcedEnv(client, classId, id => stamp.TryGetValue(id, out TypeBinding? bound) ? bound : null);
                sourcedEnv = ClassSourcedEnv(client, classId, paramId => SourcePathType(new[] { paramId }));
                origin = new NeoTypeSourceOrigin(position.ValueId, classId);
            }
            else
            {
                concreteEnv = slotEnv;
                sourcedEnv = RowSourcedEnv(
                    client,
                    classId,
                    position.Sourced,
                    ClosedClassId(position.Concrete),
                    id => concreteEnv.TryGetValue(id, out TypeBinding? bound) ? bound : null,
                    position.TemplateClassId);
            }
            // A class row in a declared generic slot stores its own type (§12.1).
            visit?.Invoke(new NeoTypeSourceRequirement(
                row,
                position,
                bindingNode ? ownParams : null,
                spelled,
                !bindingNode && ownParams.Length > 0
                    ? TypeSourceOf(
                        position.Origin,
                        RowOwnType(position.ValueId, classId, ownParams, position.Concrete, sourcedEnv),
                        RowOwnType(position.ValueId, classId, ownParams, position.Concrete, concreteEnv),
                        position.TemplateClassId)
                    : null,
                bindingNode ? classId : null));
            // A binding node with no stamp leaves its fields' types unknown.
            if (bindingNode && ownParams.Length > 0 && stored is null && spelled is null)
                return;
            // Pushed first, so a field holding the same row places it.
            if (constructorArguments)
                PushConstructorArguments(client, row, classId, sourcedEnv, concreteEnv, origin, position.TemplateClassId, push, wants);
            if (!fields || ContainerValue(row) is not Dictionary<string, string> fieldValues)
                return;
            foreach (MergedSchemaEntry entry in client.ResolveInstanceSurfaceSchema(classId))
            {
                if (!fieldValues.TryGetValue(entry.schemaKey, out string? childId) || childId is null)
                    continue;
                if (wants(childId) is null)
                    continue;
                if (!client.TryGetMember(entry.memberId, out Member? child))
                    throw new InvalidOperationException($"Class \"{classId}\" names missing member \"{entry.memberId}\" at \"{entry.schemaKey}\".");
                // No type spells it, so no row under it is typed by a param.
                if (!HasTypeBinding(client, child))
                    continue;
                push(new NeoTypeSourcePosition(
                    childId,
                    child,
                    SourcedMemberType(client, child, sourcedEnv),
                    SourcedMemberType(client, child, concreteEnv),
                    origin,
                    DeclaredMemberType(client, child),
                    position.TemplateClassId));
            }
        }

        /// <summary>
        /// Pushes each row a class row's <c>constructorArgs</c> hold (§12.1):
        /// it sits under its constructor parameter's type, read through the
        /// row's env like a field's. Scalar arguments are stored inline, so
        /// only aggregate types own rows.
        /// </summary>
        private static void PushConstructorArguments(
            NeoClient client,
            MemberValueBase row,
            string classId,
            Dictionary<string, TypeBinding> sourcedEnv,
            Dictionary<string, TypeBinding> concreteEnv,
            NeoTypeSourceOrigin? origin,
            string? templateClassId,
            Action<NeoTypeSourcePosition> push,
            Func<string, MemberValueBase?> wants)
        {
            if (row is not ObjectMemberValue { constructorArgs: { } arguments, instanceConstructorId: string constructorId } constructed)
                return;
            if (!client.constructors.TryGetValue(constructorId, out ConstructorRecord? constructor))
                throw new InvalidOperationException($"Value \"{constructed.id}\" names missing constructor \"{constructorId}\".");
            if (constructor.classId != classId)
            {
                throw new InvalidOperationException(
                    $"Value \"{constructed.id}\" of class \"{classId}\" names constructor \"{constructorId}\" of class \"{constructor.classId}\".");
            }
            for (int index = 0; index < constructor.argumentTypes.Length; index++)
            {
                string parameterId = NeoClient.ConstructorParameterId(constructor, index);
                arguments.TryGetValue(parameterId, out JToken? token);
                FunctionArgumentTypeInfo argument = constructor.argumentTypes[index];
                if (NeoScript.NeoConstructorOut.Validate(argument, token, parameterId)
                    || token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
                    continue;
                if (argument.type is not MemberKind.Generic && !ConstructorArgumentStoresRow(argument))
                    continue;
                Member parameter = NeoTypeBindingViews.View(NeoTypeBindings.ArgumentOf(NeoTypeRules.Declared(argument)));
                if (!HasTypeBinding(client, parameter))
                    continue;
                TypeBinding concrete = SourcedMemberType(client, parameter, concreteEnv);
                if (concrete.Kind != TypeBindingKind.Closed || !ConstructorArgumentStoresRow(concrete.Head!.Type))
                    continue;
                // A row id, or a variant selection naming its row. A selection
                // with no row names only an authored variant.
                if (NeoClient.ConstructorArgumentRowId(token) is not string childId)
                {
                    if (token is JObject && ClosedClassId(concrete) is string variantClassId
                        && NeoReferenceTypes.IsVariantClass(client, variantClassId))
                    {
                        continue;
                    }
                    throw new InvalidOperationException($"Value \"{constructed.id}\" stores constructor argument \"{parameterId}\" inline, but its type stores a row.");
                }
                if (wants(childId) is null)
                    continue;
                push(new NeoTypeSourcePosition(
                    childId,
                    parameter,
                    SourcedMemberType(client, parameter, sourcedEnv),
                    concrete,
                    origin,
                    DeclaredMemberType(client, parameter),
                    templateClassId));
            }
        }

        /// <summary>A binding node's stamp where its position spells the row's own class.</summary>
        private static Dictionary<string, TypeBinding>? SpelledStamp(TypeBinding concrete, string classId, string[] ownParams)
        {
            if (ownParams.Length == 0 || ClosedClassId(concrete) != classId)
                return null;
            var stamp = new Dictionary<string, TypeBinding>(ownParams.Length, StringComparer.Ordinal);
            foreach (string paramId in ownParams)
            {
                if (concrete.Bindings is null
                    || !concrete.Bindings.TryGetValue(paramId, out TypeBinding? bound)
                    || !IsFullyClosed(bound))
                {
                    return null;
                }
                stamp[paramId] = bound;
            }
            return stamp;
        }

        /// <summary>
        /// The stamp a closed slot naming one of an open row's bases, or an
        /// interface it implements, determines for it (P127 §1.2, P113
        /// §3.6), as a write into that slot stores it. Null where the slot
        /// leaves a param unbound.
        /// </summary>
        private static Dictionary<string, TypeBinding>? DeterminedStamp(NeoClient client, string classId, string[] ownParams, TypeBinding concrete)
        {
            if (ownParams.Length == 0 || ClosedClassId(concrete) == classId || !IsFullyClosed(concrete))
                return null;
            if (NeoReferenceTypes.OpenClassRowType(client, classId, NeoTypeBindings.ToTypeInfo(concrete)) is not { typeArguments: { } arguments })
                return null;
            var stamp = new Dictionary<string, TypeBinding>(ownParams.Length, StringComparer.Ordinal);
            foreach (string paramId in ownParams)
            {
                if (!arguments.TryGetValue(paramId, out TypeInfo? argument))
                    return null;
                TypeBinding bound = NeoTypeBindings.FromTypeInfo(NeoTypeRules.Declared(argument));
                if (!IsFullyClosed(bound))
                    return null;
                stamp[paramId] = bound;
            }
            return stamp;
        }

        /// <summary>A declared-slot class row's own type: its class over its own params, read in <paramref name="env"/>, with the slot's nullability.</summary>
        private static TypeBinding RowOwnType(
            string rowId,
            string classId,
            string[] ownParams,
            TypeBinding slotType,
            Dictionary<string, TypeBinding> env)
        {
            var bindings = new Dictionary<string, TypeBinding>(ownParams.Length, StringComparer.Ordinal);
            foreach (string paramId in ownParams)
            {
                if (!env.TryGetValue(paramId, out TypeBinding? bound))
                    throw new InvalidOperationException($"Value \"{rowId}\" of class \"{classId}\" sits in a slot that leaves param \"{paramId}\" unbound.");
                bindings[paramId] = bound;
            }
            bool required = slotType.Kind != TypeBindingKind.Closed || slotType.Head!.Required;
            return TypeBinding.Closed(new TypeHead(MemberKind.Class, required, classId: classId), bindings);
        }

        private static string? ClosedClassId(TypeBinding type) =>
            type.Kind == TypeBindingKind.Closed && type.Head!.Type == MemberKind.Class ? type.Head.ClassId : null;

        /// <summary>
        /// A declared-slot class row's closed environment: the slot's own
        /// arguments for its class, or the arguments a descendant row takes
        /// through its chain.
        /// </summary>
        private static Dictionary<string, TypeBinding> ConcreteClassEnv(NeoClient client, string classId, TypeBinding concrete)
        {
            var slotArguments = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            if (concrete.Kind == TypeBindingKind.Closed && concrete.Bindings is not null)
                foreach (KeyValuePair<string, TypeBinding> pair in concrete.Bindings)
                    if (pair.Value.Kind != TypeBindingKind.Definition)
                        slotArguments[pair.Key] = pair.Value;
            if (ClosedClassId(concrete) == classId)
                return ClassSourcedEnv(client, classId, id => slotArguments.TryGetValue(id, out TypeBinding? bound) ? bound : null);
            var env = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            IReadOnlyDictionary<string, NeoGenericEnvEntry> resolved = NeoGenericResolution.ResolveInstanceEnv(
                client.ResolveClassInheritanceChain(classId),
                slotArguments,
                client);
            foreach (KeyValuePair<string, NeoGenericEnvEntry> pair in resolved)
            {
                // A param the slot spells reads the slot's argument; the chain
                // restates it over the row's own params.
                if (slotArguments.TryGetValue(pair.Key, out TypeBinding? spelled))
                {
                    env[pair.Key] = spelled;
                    continue;
                }
                if (!pair.Value.IsBound)
                    continue;
                env[pair.Key] = NeoTypeBindings.ArgumentOfEntry(client, pair.Value, resolved);
            }
            return env;
        }

        private static void PushEntries(
            NeoClient client,
            MemberValueBase row,
            Member member,
            NeoTypeSourcePosition position,
            Action<NeoTypeSourcePosition> push,
            Func<string, IEnumerable<string>> unorderedEntryIds,
            Func<string, MemberValueBase?> wants)
        {
            NeoTypeSourcePosition At(string valueId, string placementId, string key) =>
                EntryPosition(client, position, valueId, placementId, key);
            object? value = ContainerValue(row);
            if (member is TupleMember tuple)
            {
                if (value is not string[] elements)
                    return;
                for (int index = 0; index < tuple.elementMemberIds.Length && index < elements.Length; index++)
                    if (elements[index] is string valueId && wants(valueId) is not null)
                        push(At(valueId, tuple.elementMemberIds[index], index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return;
            }
            if (member is DictionaryMember dictionary)
            {
                if (value is not Dictionary<string, string> entries)
                    return;
                foreach (string valueId in entries.Values)
                    if (wants(valueId) is not null)
                        push(At(valueId, dictionary.entryMemberId, "1"));
                return;
            }
            if (member is not ListMember list)
                return;
            IEnumerable<string> ids = list.ListKind == NeoListKind.Unordered
                ? unorderedEntryIds(position.ValueId)
                : value as string[] ?? Array.Empty<string>();
            foreach (string valueId in ids)
                if (valueId is not null && wants(valueId) is not null)
                    push(At(valueId, list.entryMemberId, "0"));
        }

        /// <summary>The position of entry <paramref name="valueId"/> at <paramref name="key"/> of the container at <paramref name="position"/>.</summary>
        private static NeoTypeSourcePosition EntryPosition(
            NeoClient client,
            NeoTypeSourcePosition position,
            string valueId,
            string placementId,
            string key)
        {
            if (!client.TryGetMember(placementId, out Member? placement))
                throw new InvalidOperationException($"Container \"{position.ValueId}\" names missing member \"{placementId}\".");
            return new NeoTypeSourcePosition(
                valueId,
                placement,
                SourcedEntryType(position.Sourced, key),
                SourcedEntryType(position.Concrete, key),
                position.Origin,
                DeclaredEntryType(position.Declared, key),
                position.TemplateClassId);
        }

        /// <summary>The declared type at <paramref name="key"/> inside <paramref name="declared"/>; a parameter's is itself.</summary>
        private static TypeBinding DeclaredEntryType(TypeBinding declared, string key)
        {
            if (declared.Kind != TypeBindingKind.Closed)
                return declared;
            if (declared.Bindings is null
                || !declared.Bindings.TryGetValue(key, out TypeBinding? entry)
                || entry.Kind == TypeBindingKind.Definition)
            {
                return ClosedUnknown;
            }
            return entry;
        }

        // ------------------------------------------------------------------
        // Requirements: apply and judge.
        // ------------------------------------------------------------------

        /// <summary>Writes one placed row's class, stamp and typeSource as its placement requires.</summary>
        internal static void Apply(NeoTypeSourceRequirement placed) => Apply(placed, null);

        /// <summary>
        /// <see cref="Apply(NeoTypeSourceRequirement)"/> for a row that may
        /// have moved: one that becomes a binding node keeps the class type
        /// its typeSource resolved to, read through <paramref name="lookup"/>
        /// before the typeSource goes.
        /// </summary>
        private static void Apply(NeoTypeSourceRequirement placed, Func<string, MemberValueBase?>? lookup)
        {
            MemberValueBase row = placed.Row;
            // A binding node always stores its class (§12.1).
            if (placed.BindingClassId is not null)
                row.classId = placed.BindingClassId;
            var stamped = row as MemberValue;
            if (placed.StampParams is null || placed.StampParams.Length == 0)
            {
                if (stamped is not null)
                    stamped.genericBindings = null;
            }
            else if (stamped?.genericBindings is null)
            {
                Dictionary<string, TypeBinding>? stamp = placed.SpelledStamp
                    ?? (lookup is null ? null : RowOwnStamp(placed.RowId, row, lookup));
                if (stamp is null || stamped is null)
                    throw new InvalidOperationException($"Binding node \"{placed.RowId}\" stores no stamp, and its slot doesn't spell one.");
                stamped.genericBindings = stamp;
            }
            if (!Equals(row.typeSource, placed.TypeSource))
                row.typeSource = placed.TypeSource;
        }

        /// <summary>The placement rule's refusal for one placed row, or null (§12.2).</summary>
        internal static string? Violation(NeoTypeSourceRequirement placed)
        {
            MemberValueBase row = placed.Row;
            string label = $"Value \"{placed.RowId}\"";
            if (placed.BindingClassId is not null && row.classId is null)
                return $"{label} is a binding node and stores no classId.";
            Dictionary<string, TypeBinding>? stamp = (row as MemberValue)?.genericBindings;
            string[]? stampParams = placed.StampParams;
            if (stampParams is null || stampParams.Length == 0)
            {
                if (stamp is not null)
                {
                    return stampParams is null
                        ? $"{label} stores a stamp but isn't a binding node."
                        : $"{label} stores a stamp, but its class closes no type parameter.";
                }
            }
            else
            {
                if (stamp is null)
                    return $"{label} is a binding node and stores no stamp.";
                string stampedKeys = string.Join(", ", stamp.Keys.OrderBy(key => key, StringComparer.Ordinal));
                string paramKeys = string.Join(", ", stampParams.OrderBy(key => key, StringComparer.Ordinal));
                if (stampedKeys != paramKeys)
                    return $"{label} stamps [{stampedKeys}], but its class closes [{paramKeys}].";
            }
            NeoTypeSource? stored = row.typeSource;
            NeoTypeSource? expected = placed.TypeSource;
            if (expected is null)
                return stored is null ? null : $"{label} stores typeSource, but its declared type names no type parameter.";
            if (stored is null)
                return $"{label} stores no typeSource, but its declared type names a type parameter.";
            if (stored.Type is not null && stored.ValueId is null && HasOpenArm(stored.Type))
            {
                // Only a member default's descendants are templates.
                if (placed.Position.TemplateClassId is not string template)
                    return $"{label} typeSource.type is open but names no valueId outside a member default.";
                if (ForeignOpenParam(stored.Type, template) is string foreign)
                    return $"{label} typeSource.type names param \"{foreign}\" of another class, but its template default belongs to class \"{template}\".";
            }
            if (stored.ValueId is null && expected.ValueId is not null)
                return $"{label} typeSource names no row, but its placement reads it from \"{expected.ValueId}\".";
            if (stored.ValueId is not null && expected.ValueId is null)
                return $"{label} typeSource names \"{stored.ValueId}\", but its placement closes its type.";
            if (stored.ValueId != expected.ValueId)
                return $"{label} typeSource names \"{stored.ValueId}\", but its nearest binding node or box row is \"{expected.ValueId}\".";
            if (expected.Path is not null)
            {
                if (stored.Path is null)
                    return $"{label} typeSource stores a type where its placement derives path {PathJson(expected.Path)}.";
                if (!stored.Path.SequenceEqual(expected.Path, StringComparer.Ordinal))
                    return $"{label} typeSource.path {PathJson(stored.Path)} is stale; its placement derives {PathJson(expected.Path)}.";
                return null;
            }
            if (stored.Type is null)
                return $"{label} typeSource stores a path where its placement derives a type.";
            if (!stored.Type.Equals(expected.Type))
                return $"{label} typeSource.type is stale; its placement derives {Key(expected.Type!)}.";
            return null;
        }

        private static string PathJson(IReadOnlyList<string> path) => JsonConvert.SerializeObject(path);

        /// <summary>The first param <paramref name="type"/> leaves open that isn't class <paramref name="classId"/>'s, or null.</summary>
        private static string? ForeignOpenParam(TypeBinding type, string classId)
        {
            string? foreign = null;
            Substitute(type, open =>
            {
                bool owned = open.Definition!.Type == MemberKind.Class && open.Definition.ClassId == classId;
                if (!owned && foreign is null)
                    foreign = open.Param;
                return null;
            });
            return foreign;
        }

        /// <summary>A binding's canonical JSON, its keys sorted: the server's <c>typeBindingKey</c>, for messages.</summary>
        internal static string Key(TypeBinding binding)
        {
            var writer = new JTokenWriter();
            TypeBindings.Write(writer, binding);
            return Canonical(writer.Token!);
        }

        private static string Canonical(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    return "{" + string.Join(",", obj.Properties()
                        .Where(property => property.Value.Type != JTokenType.Undefined)
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property => JsonConvert.SerializeObject(property.Name) + ":" + Canonical(property.Value))) + "}";
                case JArray array:
                    return "[" + string.Join(",", array.Select(Canonical)) + "]";
                default:
                    return token.ToString(Formatting.None);
            }
        }

        // ------------------------------------------------------------------
        // Readers: one hop, never an owner climb.
        // ------------------------------------------------------------------

        /// <summary>
        /// A placed row's type, read from the row alone: its typeSource
        /// (following a pointer one hop through <paramref name="lookup"/>), or
        /// its member's own type when that names no parameter. A template
        /// row's type stays open over its owning class's params.
        /// </summary>
        internal static TypeBinding PlacedRowType(
            NeoClient client,
            string rowId,
            MemberValueBase row,
            Member member,
            Func<string, MemberValueBase?> lookup)
        {
            NeoTypeSource? typeSource = row.typeSource;
            if (typeSource is null)
            {
                return ArgumentOfMember(client, member, paramId =>
                    throw new InvalidOperationException($"Value \"{rowId}\" under member \"{member.id}\" is missing typeSource."));
            }
            return TypeSourceType(rowId, typeSource, typeSource.ValueId is string targetId ? lookup(targetId) : null);
        }

        /// <summary>The type <paramref name="typeSource"/> resolves to, given the row it names. A type with no row needs no target.</summary>
        internal static TypeBinding TypeSourceType(string rowId, NeoTypeSource typeSource, MemberValueBase? target)
        {
            if (typeSource.Type is not null && typeSource.ValueId is null)
                return typeSource.Type;
            string targetId = typeSource.ValueId!;
            if (target is null)
                throw new InvalidOperationException($"Value \"{rowId}\" typeSource names missing row \"{targetId}\".");
            Dictionary<string, TypeBinding>? stamp = (target as MemberValue)?.genericBindings;
            if (typeSource.Path is not null)
            {
                return BindingAtPath(stamp, target.valueType, typeSource.Path)
                    ?? throw new InvalidOperationException($"Value \"{rowId}\" typeSource.path {PathJson(typeSource.Path)} isn't bound by its target.");
            }
            if (stamp is null)
                throw new InvalidOperationException($"Value \"{rowId}\" typeSource names row \"{targetId}\", which stores no stamp.");
            return Substitute(typeSource.Type!, open =>
                stamp.TryGetValue(open.Param!, out TypeBinding? bound)
                    ? bound
                    : throw new InvalidOperationException(
                        $"Value \"{rowId}\" typeSource.type names param \"{open.Param}\", which row \"{targetId}\" doesn't stamp."));
        }

        /// <summary>The closed type <paramref name="typeSource"/> resolves to, or null while its target is missing or leaves a param unbound.</summary>
        internal static TypeBinding? TryTypeSourceType(NeoTypeSource typeSource, Func<string, MemberValueBase?> lookup)
        {
            if (typeSource.Type is not null && typeSource.ValueId is null)
                return typeSource.Type;
            if (lookup(typeSource.ValueId!) is not { } target)
                return null;
            Dictionary<string, TypeBinding>? stamp = (target as MemberValue)?.genericBindings;
            TypeBinding? type = typeSource.Path is not null
                ? BindingAtPath(stamp, target.valueType, typeSource.Path)
                : stamp is null ? null
                : Substitute(typeSource.Type!, open => stamp.TryGetValue(open.Param!, out TypeBinding? bound) ? bound : null);
            return type is not null && IsFullyClosed(type) ? TypeBindings.WithoutNames(type) : null;
        }

        /// <summary>A stamp binds a path's first key; a box's <c>valueType</c> is its root.</summary>
        private static TypeBinding? BindingAtPath(
            IReadOnlyDictionary<string, TypeBinding>? stamp,
            TypeBinding? valueType,
            IReadOnlyList<string> path)
        {
            TypeBinding? binding;
            int start;
            if (stamp is null)
            {
                binding = valueType;
                start = 0;
            }
            else
            {
                binding = path.Count > 0 && stamp.TryGetValue(path[0], out TypeBinding? bound) ? bound : null;
                start = 1;
            }
            for (int index = start; index < path.Count && binding is not null; index++)
            {
                TypeBinding? inner = binding.Kind == TypeBindingKind.Closed && binding.Bindings is not null
                    && binding.Bindings.TryGetValue(path[index], out TypeBinding? found)
                    ? found
                    : null;
                binding = inner?.Kind == TypeBindingKind.Definition ? null : inner;
            }
            return binding;
        }

        /// <summary>A class row's environment from its typeSource alone, including constructor arguments with no member.</summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> TypeSourcedClassRowEnv(
            NeoClient client, MemberValue row, string classId) =>
            NeoGenericResolution.EnvFromStamp(client, TypeSourcedClassRowStamp(client, row, classId));

        internal static Dictionary<string, TypeBinding> TypeSourcedClassRowStamp(
            NeoClient client, MemberValue row, string classId)
        {
            NeoTypeSource source = row.typeSource
                ?? throw new InvalidOperationException($"Value \"{row.id}\" stores no typeSource.");
            MemberValue? target = source.ValueId is string id && client.TryGetValue(id, out MemberValue? found) ? found : null;
            return ClosedOnly(ConcreteClassEnv(client, classId, TypeSourceType(row.id, source, target)));
        }

        /// <summary>
        /// The closed bindings a placed row's members read in, as a stamp: a
        /// class row's params from its stamp or type, and a container's from
        /// matching its member's declared type against its resolved type.
        /// A param a template row leaves open is left out.
        /// </summary>
        internal static Dictionary<string, TypeBinding> PlacedRowStamp(
            NeoClient client,
            string rowId,
            MemberValueBase row,
            Member member,
            Func<string, MemberValueBase?> lookup)
        {
            string? classId = row.classId ?? (member as ClassMember)?.DeclaredClassId;
            if (classId is not null && (row as MemberValue)?.genericBindings is { } stamp)
                return ClosedOnly(ClassSourcedEnv(client, classId, id => stamp.TryGetValue(id, out TypeBinding? bound) ? bound : null));
            TypeBinding type = PlacedRowType(client, rowId, row, member, lookup);
            if (classId is not null)
                return ClosedOnly(ConcreteClassEnv(client, classId, type));
            var env = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            MatchDeclared(rowId, DeclaredMemberType(client, member), type, env);
            return ClosedOnly(env);
        }

        /// <summary>
        /// The closed bindings <paramref name="row"/>'s own members read in,
        /// from its record alone (§12.1): a stamp, a typeSource one hop away,
        /// a class that has no params of its own, or a container whose
        /// declared type names none. Throws for a row that needs a typeSource
        /// and stores none.
        /// </summary>
        internal static Dictionary<string, TypeBinding> OwnStamp(
            NeoClient client,
            string rowId,
            MemberValueBase row,
            Member member,
            Func<string, MemberValueBase?> lookup)
        {
            if (row.typeSource is not null)
                return PlacedRowStamp(client, rowId, row, member, lookup);
            RequireUnsourced(client, rowId, row, member);
            string? classId = row.classId ?? (member as ClassMember)?.DeclaredClassId;
            if (classId is null)
                return new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            Dictionary<string, TypeBinding>? own = (row as MemberValue)?.genericBindings;
            return ClosedOnly(ClassSourcedEnv(client, classId, id => own is not null && own.TryGetValue(id, out TypeBinding? bound) ? bound : null));
        }

        /// <summary>
        /// Refuses a row with no typeSource whose type its record must hold
        /// (§12.1): a class row with params of its own and no stamp, or a
        /// container whose declared type names a param.
        /// </summary>
        private static void RequireUnsourced(NeoClient client, string rowId, MemberValueBase row, Member member)
        {
            // A null slot holds nothing a type parameter types.
            if (ContainerValue(row) is null)
                return;
            Member declared = SchemaMember(client, member);
            string? classId = row.classId ?? (declared as ClassMember)?.DeclaredClassId;
            if (classId is not null)
            {
                if ((row as MemberValue)?.genericBindings is not null)
                    return;
                IList<NeoSchemaClass> chain = client.ResolveClassInheritanceChain(classId);
                if (chain.Count > 0 && chain[0].genericParams is { Count: > 0 })
                    throw new InvalidOperationException($"Class row \"{rowId}\" under member \"{declared.id}\" stores neither a stamp nor a typeSource.");
                return;
            }
            if (declared is ListMember or DictionaryMember or TupleMember
                && HasTypeBinding(client, declared)
                && HasOpenArm(DeclaredMemberType(client, declared)))
            {
                throw new InvalidOperationException($"Value \"{rowId}\" under member \"{declared.id}\" is missing typeSource.");
            }
        }

        /// <summary>
        /// The closed bindings of a class row's own params, read from the row
        /// alone: a binding node's stamp, or the class type its typeSource
        /// resolves to, one hop away. Null when it stores neither.
        /// </summary>
        internal static Dictionary<string, TypeBinding>? RowOwnStamp(
            string rowId,
            MemberValueBase row,
            Func<string, MemberValueBase?> lookup)
        {
            if ((row as MemberValue)?.genericBindings is { } stamp)
                return stamp;
            if (row.typeSource is not NeoTypeSource typeSource)
                return null;
            TypeBinding type = TypeSourceType(rowId, typeSource, typeSource.ValueId is string targetId ? lookup(targetId) : null);
            if (type.Kind != TypeBindingKind.Closed || type.Head!.Type != MemberKind.Class)
                return null;
            var own = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            if (type.Bindings is not null)
                foreach (KeyValuePair<string, TypeBinding> pair in type.Bindings)
                    if (IsFullyClosed(pair.Value))
                        own[pair.Key] = pair.Value;
            return own;
        }

        /// <summary>The stamp of the binding node <paramref name="row"/>'s typeSource names, one hop away, or null.</summary>
        internal static Dictionary<string, TypeBinding>? OriginStamp(NeoClient client, MemberValueBase row) =>
            row.typeSource?.ValueId is string originId && client.TryGetValue(originId, out MemberValue? origin)
                ? origin.genericBindings
                : null;

        /// <summary>
        /// The first reason a stored typeSource doesn't fit its target, or
        /// null (§12.2): the target holds a binding, a path stays inside it,
        /// and a type names only the target's stamped class's params.
        /// </summary>
        internal static string? TargetViolation(string rowId, NeoTypeSource typeSource, MemberValueBase? target)
        {
            string label = $"Value \"{rowId}\" typeSource";
            if (typeSource.ValueId is null)
                return null;
            if (target is null)
                return $"{label} names missing row \"{typeSource.ValueId}\".";
            Dictionary<string, TypeBinding>? stamp = (target as MemberValue)?.genericBindings;
            TypeBinding? box = target.valueType;
            if (stamp is null && box is null)
                return $"{label} names row \"{typeSource.ValueId}\", which holds no stamp or valueType.";
            if (typeSource.Path is not null)
            {
                return BindingAtPath(stamp, box, typeSource.Path) is null
                    ? $"{label}.path {PathJson(typeSource.Path)} isn't bound by row \"{typeSource.ValueId}\"."
                    : null;
            }
            if (box is not null)
                return $"{label}.type names box row \"{typeSource.ValueId}\", which stamps no params.";
            string? foreign = null;
            Substitute(typeSource.Type!, open =>
            {
                bool owned = open.Definition!.Type == MemberKind.Class
                    && open.Definition.ClassId == target.classId
                    && stamp!.ContainsKey(open.Param!);
                if (!owned && foreign is null)
                    foreign = open.Param;
                return null;
            });
            return foreign is null ? null : $"{label}.type names param \"{foreign}\", which row \"{typeSource.ValueId}\" doesn't stamp.";
        }

        /// <summary>Binds each param <paramref name="declared"/> names to the part of <paramref name="type"/> it matches.</summary>
        private static void MatchDeclared(string rowId, TypeBinding declared, TypeBinding type, Dictionary<string, TypeBinding> env)
        {
            if (declared.Kind == TypeBindingKind.Open)
            {
                env[declared.Param!] = type;
                return;
            }
            if (type.Kind != TypeBindingKind.Closed)
                throw new InvalidOperationException($"Value \"{rowId}\" resolves to an open type where its member declares {Key(declared)}.");
            if (declared.Bindings is null)
                return;
            foreach (KeyValuePair<string, TypeBinding> pair in declared.Bindings)
            {
                if (pair.Value.Kind == TypeBindingKind.Definition)
                    continue;
                if (type.Bindings is null
                    || !type.Bindings.TryGetValue(pair.Key, out TypeBinding? matched)
                    || matched.Kind == TypeBindingKind.Definition)
                {
                    throw new InvalidOperationException($"Value \"{rowId}\" resolves to a type that binds no '{pair.Key}' where its member declares one.");
                }
                MatchDeclared(rowId, pair.Value, matched, env);
            }
        }

        private static Dictionary<string, TypeBinding> ClosedOnly(Dictionary<string, TypeBinding> env)
        {
            List<string>? open = null;
            foreach (KeyValuePair<string, TypeBinding> pair in env)
                if (!IsFullyClosed(pair.Value))
                    (open ??= new List<string>()).Add(pair.Key);
            if (open is not null)
                foreach (string paramId in open)
                    env.Remove(paramId);
            return env;
        }

        /// <summary>
        /// The stamp a container row's entries (or a class row's fields) read
        /// in: its own stamp, or the bindings its typeSource resolves to,
        /// one hop away. Null when its declarations close its type or the
        /// bindings close nothing, so the caller's environment stands; throws
        /// for a row that needs a typeSource and stores none. Cached on the
        /// row per member.
        /// </summary>
        internal static IReadOnlyDictionary<string, TypeBinding>? EntryStamp(NeoClient client, MemberValueBase? row, Member member)
        {
            if (row is null)
                return null;
            if ((row as MemberValue)?.genericBindings is { } stamp)
                return stamp;
            if (row.placedStamp is { } cached && cached.memberId == member.id)
                return cached.stamp;
            string rowId = (row as MemberValue)?.id ?? member.id;
            IReadOnlyDictionary<string, TypeBinding>? result = null;
            if (row.typeSource is null)
            {
                RequireUnsourced(client, rowId, row, member);
            }
            else
            {
                Dictionary<string, TypeBinding> placed = PlacedRowStamp(
                    client,
                    rowId,
                    row,
                    member,
                    id => client.TryGetValue(id, out MemberValue? target) ? target : null);
                result = placed.Count == 0 ? null : placed;
            }
            row.placedStamp = (member.id, result);
            return result;
        }

        // ------------------------------------------------------------------
        // Writers: place what a write creates, top-down from where it sits.
        // ------------------------------------------------------------------

        /// <summary>
        /// A stored row's position, read from the row alone: a binding node
        /// owns its own type, a row with a typeSource reads it one hop away,
        /// and any other row's declarations close its type. The compose
        /// <c>placedRowTypeSourcePosition</c>; never climbs owners.
        /// </summary>
        internal static NeoTypeSourcePosition? PlacedRowPosition(
            NeoClient client,
            string rowId,
            MemberValueBase row,
            Member member,
            Func<string, MemberValueBase?> lookup)
        {
            if ((row as MemberValue)?.genericBindings is not null)
                return OwnerlessPosition(rowId);
            Member placement = SchemaMember(client, member);
            if (row.typeSource is NeoTypeSource typeSource)
            {
                MemberValueBase? target = typeSource.ValueId is string targetId ? lookup(targetId) : null;
                NeoTypeSourceOrigin? origin = typeSource.ValueId is string originId
                    ? new NeoTypeSourceOrigin(originId, target?.classId)
                    : null;
                TypeBinding concrete = TypeSourceType(rowId, typeSource, target);
                return new NeoTypeSourcePosition(
                    rowId,
                    placement,
                    typeSource.Path is not null ? SourcePathType(typeSource.Path) : SourcedOverOrigin(typeSource.Type!, origin),
                    concrete,
                    origin,
                    DeclaredMemberType(client, placement),
                    origin is null ? TemplateClassOf(concrete) : null);
            }
            if (row.classId is not null && IsBindingNodePlacement(client, placement))
                return OwnerlessPosition(rowId);
            if (!HasTypeBinding(client, placement))
                return null;
            TypeBinding type = PlacedRowType(client, rowId, row, placement, lookup);
            return new NeoTypeSourcePosition(rowId, placement, type, type, null, DeclaredMemberType(client, placement), null);
        }

        /// <summary>The schema declaration a node's member stands for: a substituted member keeps its slot's id.</summary>
        private static Member SchemaMember(NeoClient client, Member member) =>
            !string.IsNullOrEmpty(member.id) && client.TryGetMember(member.id, out Member? declared) ? declared : member;

        /// <summary>A stored <c>typeSource.type</c> read back as its source's binding paths.</summary>
        private static TypeBinding SourcedOverOrigin(TypeBinding type, NeoTypeSourceOrigin? origin)
        {
            if (origin?.ClassId is not string classId)
                return type;
            return Substitute(type, open =>
                open.Definition!.Type == MemberKind.Class && open.Definition.ClassId == classId
                    ? SourcePathType(new[] { open.Param! })
                    : null);
        }

        /// <summary>The class whose params a template row's type leaves open, or null for a closed type.</summary>
        private static string? TemplateClassOf(TypeBinding type)
        {
            if (type.Kind == TypeBindingKind.Open)
                return type.Definition!.ClassId;
            if (type.Bindings is not null)
                foreach (TypeBinding inner in type.Bindings.Values)
                    if (inner.Kind != TypeBindingKind.Definition && TemplateClassOf(inner) is string classId)
                        return classId;
            return null;
        }

        /// <summary>
        /// The position of <paramref name="childId"/> under
        /// <paramref name="parent"/>, derived as the walk derives a child's:
        /// one hop through the parent's stamp or typeSource. Every entry of a
        /// list or dictionary shares one position, so a pending collection
        /// that doesn't hold its new entry yet still places it; any other
        /// parent's value must already hold the child. Null where no type
        /// spells the child. The compose <c>childTypeSourcePosition</c>.
        /// </summary>
        internal static NeoTypeSourcePosition? ChildPosition(
            NeoClient client,
            string childId,
            string parentId,
            MemberValueBase parent,
            Member parentMember,
            Func<string, MemberValueBase?> lookup)
        {
            if (PlacedRowPosition(client, parentId, parent, parentMember, lookup) is not NeoTypeSourcePosition position)
                return null;
            if (parent.valueType is null && parent.classId is null)
            {
                Member container = TypedMember(position.Placement, position.Concrete);
                if (container is ListMember list)
                    return EntryPosition(client, position, childId, list.entryMemberId, "0");
                if (container is DictionaryMember dictionary)
                    return EntryPosition(client, position, childId, dictionary.entryMemberId, "1");
            }
            NeoTypeSourcePosition? found = null;
            string[] only = { childId };
            // A write links the child into the parent's value; its
            // constructor arguments are other rows.
            Place(
                client,
                parent,
                position,
                null,
                child => found ??= child.ValueId == childId ? child : null,
                _ => only,
                id => id == childId ? parent : null,
                false);
            return found;
        }

        /// <summary>
        /// The typeSource entry <paramref name="entry"/>, which no store
        /// holds, would take in <paramref name="collection"/>, such as a
        /// probed Set entry's. Null where its placement stores none.
        /// </summary>
        internal static NeoTypeSource? EntryTypeSource(NeoClient client, MemberValue collection, Member collectionMember, MemberValue entry)
        {
            Func<string, MemberValueBase?> lookup = id => client.TryGetValue(id, out MemberValue? row) ? row : null;
            if (ChildPosition(client, entry.id, collection.id, collection, collectionMember, lookup) is not NeoTypeSourcePosition position)
                return null;
            NeoTypeSource? typeSource = null;
            Place(
                client,
                entry,
                position,
                placed => typeSource = ReferenceEquals(placed.Row, entry) ? placed.TypeSource : typeSource,
                _ => { },
                _ => Array.Empty<string>(),
                _ => null,
                false);
            return typeSource;
        }

        /// <summary>
        /// Places the rows a write just created under
        /// <paramref name="parent"/>, which already links
        /// <paramref name="childId"/>, as the compose
        /// <c>placeCreatedTypeSources</c> does: from the child's position, top
        /// down through every row under it. A committed row whose placement
        /// changes, such as one an import moved, is staged silently.
        /// </summary>
        internal static void PlaceCreatedUnder(
            NeoClient client,
            NeoWritePlan plan,
            string parentId,
            MemberValueBase parent,
            Member parentMember,
            string childId)
        {
            if (plan.Resolve(childId) is not (ObjectMemberValue or ArrayMemberValue))
                return;
            var graph = new PlanGraph(client, plan);
            Walk(client, graph, ChildPosition(client, childId, parentId, parent, parentMember, graph.Row), graph.Apply);
        }

        /// <summary>
        /// Places the rows a write just created at <paramref name="childId"/>,
        /// from the stored placement of the parent the write linked it under,
        /// one hop away: a member or static root's declarations close its
        /// type, and a row no owner holds is ownerless.
        /// </summary>
        internal static void PlaceCreated(NeoClient client, NeoWritePlan plan, NeoValueOwnership ownership, string childId)
        {
            if (plan.Resolve(ownership, childId) is not (ObjectMemberValue or ArrayMemberValue))
                return;
            using (client.ReadCandidate(plan))
            {
                if (!client.TryFindOwnedParent(ownership, childId, out string? parentId, out NeoValueOwnership parentOwnership))
                {
                    PlaceOwnerless(client, plan, childId);
                    return;
                }
                if (RootMemberId(parentId, "member:") is string memberId)
                {
                    PlaceCreatedRoot(client, plan, childId, client.members[memberId], declaringClassId: null);
                    return;
                }
                if (RootMemberId(parentId, "static:") is string staticId)
                {
                    PlaceCreatedRoot(client, plan, childId, client.members[staticId], client.SchemaOwnerClassId(staticId));
                    return;
                }
                MemberValue parent = plan.Resolve(parentOwnership, parentId)
                    ?? throw new InvalidOperationException($"Created row \"{childId}\" names missing parent \"{parentId}\".");
                Member? parentMember = client.TryInferMemberForValueId(parentId, out Member? inferred)
                    ? inferred
                    : parent.classId is string classId ? NeoGeneratedTypesSupport.UnplacedClassMember(client, classId, null, parent) : null;
                if (parentMember is null)
                    throw new InvalidOperationException($"Created row \"{childId}\" sits under \"{parentId}\", whose member is unknown.");
                PlaceCreatedUnder(client, plan, parentId, parent, parentMember, childId);
            }
        }

        /// <summary>Places a created row no member holds, such as a NeoScript local's entry: a binding node.</summary>
        internal static void PlaceOwnerless(NeoClient client, NeoWritePlan plan, string rowId)
        {
            var graph = new PlanGraph(client, plan);
            Walk(client, graph, OwnerlessPosition(rowId), graph.Apply);
        }

        /// <summary>
        /// Places a P75 replay's virtual <paramref name="rows"/> under their
        /// instance <paramref name="root"/>'s stored position. The replay
        /// built them under an ownerless temporary root, whose stamp the
        /// instance root needn't store. Rows the store holds keep their own.
        /// A sparse stored class row omits the fields the replay rebuilt:
        /// <paramref name="links"/> holds them, by field, per stored row. A
        /// stored row can also drop a replayed row, such as a stored list
        /// that removed a default entry, which a later write may restore:
        /// <paramref name="parents"/>, built only when one is, places it
        /// under its replayed parent.
        /// </summary>
        internal static void PlaceVirtual(
            NeoClient client,
            MemberValue root,
            Member member,
            IReadOnlyDictionary<string, MemberValue> rows,
            IReadOnlyDictionary<string, Dictionary<string, string>> links,
            Func<IReadOnlyDictionary<string, (string ParentId, Member ParentMember)>> parents,
            NeoValueOwnership ownership)
        {
            var unordered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (MemberValue row in rows.Values)
            {
                if (row.containerId is not string containerId)
                    continue;
                if (!unordered.TryGetValue(containerId, out List<string>? entries))
                    unordered[containerId] = entries = new List<string>();
                entries.Add(row.id);
            }
            Func<string, MemberValueBase?> lookup = id =>
                id == root.id ? root
                : rows.TryGetValue(id, out MemberValue? row) ? row
                : client.TryGetValue(id, out MemberValue? stored) ? stored
                : null;
            var graph = new RowsGraph(
                id => rows.TryGetValue(id, out MemberValue? row) ? row : Linked(lookup(id), id),
                id => unordered.TryGetValue(id, out List<string>? entries)
                    ? entries
                    : client.EnumerateContainerMemberValueIds(ownership, id));
            var placed = new HashSet<string>(StringComparer.Ordinal);
            Action<NeoTypeSourceRequirement> visit = requirement =>
            {
                placed.Add(requirement.RowId);
                if (requirement.RowId != root.id && rows.ContainsKey(requirement.RowId))
                    Apply(requirement, lookup);
            };
            Walk(client, graph, PlacedRowPosition(client, root.id, root, member, lookup), visit);
            IReadOnlyDictionary<string, (string ParentId, Member ParentMember)>? replayParents = null;
            foreach (string rowId in rows.Keys)
                PlaceDropped(rowId);

            // A replayed row no stored row reaches sits where the replay put it.
            void PlaceDropped(string rowId)
            {
                if (placed.Contains(rowId) || !(replayParents ??= parents()).TryGetValue(rowId, out var parent))
                    return;
                if (rows.ContainsKey(parent.ParentId))
                    PlaceDropped(parent.ParentId);
                if (placed.Contains(rowId) || graph.Row(parent.ParentId) is not MemberValueBase parentRow)
                    return;
                Walk(client, graph, ChildPosition(client, rowId, parent.ParentId, parentRow, parent.ParentMember, graph.Row), visit);
            }

            // The walk reads a stored row with its replayed fields linked in;
            // the fields it stores win.
            MemberValueBase? Linked(MemberValueBase? row, string id)
            {
                if (row is not ObjectMemberValue stored || !links.TryGetValue(id, out Dictionary<string, string>? fields))
                    return row;
                var linked = (ObjectMemberValue)NeoClient.CloneValueRow(stored);
                linked.value = new Dictionary<string, string>(fields, StringComparer.Ordinal);
                if (stored.value is not null)
                    foreach (var pair in stored.value)
                        linked.value[pair.Key] = pair.Value;
                return linked;
            }
        }

        private static string? RootMemberId(string parentId, string prefix) =>
            parentId.StartsWith(prefix, StringComparison.Ordinal) ? parentId.Substring(prefix.Length) : null;

        /// <summary>Places a member root a write created, such as a parentless node's: its declarations close its type.</summary>
        internal static void PlaceCreatedRoot(NeoClient client, NeoWritePlan plan, string rootId, Member member) =>
            PlaceCreatedRoot(client, plan, rootId, member, client.SchemaOwnerClassId(member.id));

        private static void PlaceCreatedRoot(NeoClient client, NeoWritePlan plan, string rootId, Member member, string? declaringClassId)
        {
            if (plan.Resolve(rootId) is not (ObjectMemberValue or ArrayMemberValue))
                return;
            var graph = new PlanGraph(client, plan);
            Walk(client, graph, RootPosition(client, rootId, SchemaMember(client, member), declaringClassId), graph.Apply);
        }

        /// <summary>
        /// Places a construction's rows before it publishes: a fresh
        /// construction is ownerless, so its root is a binding node and every
        /// row under it takes the type its stamp gives it. A row the
        /// construction imported is already in Session, so one whose
        /// placement changes gets a placed copy, returned for the caller to
        /// commit once the construction publishes: its typeSource may name
        /// a staged row.
        /// </summary>
        internal static List<MemberValue>? PlaceConstructed(NeoClient client, ObjectMemberValue root, IReadOnlyList<MemberValue> rows) =>
            PlaceConstructedRows(client, rows, new[] { OwnerlessPosition(root.id) });

        internal static List<MemberValue>? PlaceConstructedArgument(
            NeoClient client, string rootId, Member member, IReadOnlyList<MemberValue> rows) =>
            PlaceConstructedRows(client, rows, new[] { RootPosition(client, rootId, member)! });

        /// <summary>Places only newly recorded arguments; fields were placed when the graph was prepared.</summary>
        internal static List<MemberValue>? PlaceConstructorArguments(NeoClient client, ObjectMemberValue root)
        {
            if (root.instanceConstructorId is not string constructorId
                || root.constructorArgs is not { Count: > 0 }
                || !client.TryGetConstructor(constructorId, out ConstructorRecord? constructor)
                || !constructor.argumentTypes.Any(parameter =>
                    parameter.type == MemberKind.Generic || ConstructorArgumentStoresRow(parameter)))
                return null;

            MemberValueBase? Lookup(string id) => client.TryGetValue(NeoValueOwnership.Session, id, out MemberValue? row) ? row : null;
            var positions = new List<NeoTypeSourcePosition>();
            NeoTypeSourcePosition position = root.typeSource is null
                ? OwnerlessPosition(root.id)
                : PlacedRowPosition(client, root.id, root,
                    NeoTypeBindingViews.View(TypeSourceType(root.id, root.typeSource,
                        root.typeSource.ValueId is string originId ? Lookup(originId) : null)), Lookup)!;
            PlaceClassRow(client, root, constructor.classId, position, root.typeSource is null,
                null, positions.Add, Lookup, constructorArguments: true, fields: false);
            // A field or collection entry already gives this row its placement.
            // Consult parent edges, without walking the constructed root's fields.
            positions.RemoveAll(argumentPosition => Lookup(argumentPosition.ValueId) is MemberValue { containerId: not null }
                || client.HasFieldOrEntryPlacementParent(argumentPosition.ValueId));
            return positions.Count == 0 ? null : PlaceConstructedRows(client, Array.Empty<MemberValue>(), positions);
        }

        private static List<MemberValue>? PlaceConstructedRows(
            NeoClient client, IReadOnlyList<MemberValue> rows, IEnumerable<NeoTypeSourcePosition> positions)
        {
            var staged = new Dictionary<string, MemberValue>(rows.Count, StringComparer.Ordinal);
            var unordered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (MemberValue row in rows)
            {
                staged[row.id] = row;
                if (row.containerId is not string containerId)
                    continue;
                if (!unordered.TryGetValue(containerId, out List<string>? entries))
                    unordered[containerId] = entries = new List<string>();
                entries.Add(row.id);
            }
            Dictionary<string, MemberValue>? imports = null;
            var graph = new RowsGraph(
                id => staged.TryGetValue(id, out MemberValue? row) ? row
                    : imports is not null && imports.TryGetValue(id, out MemberValue? copy) ? copy
                    : client.TryGetValue(NeoValueOwnership.Session, id, out MemberValue? imported) ? imported : null,
                id => unordered.TryGetValue(id, out List<string>? entries)
                    ? entries
                    : client.EnumerateContainerMemberValueIds(NeoValueOwnership.Session, id));
            Walk(client, graph, positions, placed =>
            {
                if (staged.ContainsKey(placed.RowId) || !Changes(placed))
                {
                    Apply(placed, graph.Row);
                    return;
                }
                MemberValue copy = client.CloneRowForWrite((MemberValue)placed.Row);
                Apply(
                    new NeoTypeSourceRequirement(
                        copy,
                        placed.Position,
                        placed.StampParams,
                        placed.SpelledStamp,
                        placed.TypeSource,
                        placed.BindingClassId),
                    graph.Row);
                (imports ??= new Dictionary<string, MemberValue>(StringComparer.Ordinal))[copy.id] = copy;
            });
            return imports is null ? null : new List<MemberValue>(imports.Values);
        }

        /// <summary>Whether applying <paramref name="placed"/> would change its row.</summary>
        private static bool Changes(NeoTypeSourceRequirement placed)
        {
            MemberValueBase row = placed.Row;
            if (placed.BindingClassId is not null && row.classId != placed.BindingClassId)
                return true;
            Dictionary<string, TypeBinding>? stamp = (row as MemberValue)?.genericBindings;
            if (placed.StampParams is null || placed.StampParams.Length == 0 ? stamp is not null : stamp is null)
                return true;
            return !Equals(row.typeSource, placed.TypeSource);
        }

        /// <summary>The rows a walk reads from a list of them and the store.</summary>
        private sealed class RowsGraph : INeoTypeSourceGraph
        {
            private readonly Func<string, MemberValueBase?> row;
            private readonly Func<string, IEnumerable<string>> unorderedEntryIds;

            internal RowsGraph(Func<string, MemberValueBase?> row, Func<string, IEnumerable<string>> unorderedEntryIds)
            {
                this.row = row;
                this.unorderedEntryIds = unorderedEntryIds;
            }

            public MemberValueBase? Row(string valueId) => row(valueId);

            public IEnumerable<string> UnorderedEntryIds(string containerId) => unorderedEntryIds(containerId);
        }

        /// <summary>The rows a write plan reads, staging each committed row its placement changes.</summary>
        private sealed class PlanGraph : INeoTypeSourceGraph
        {
            private readonly NeoClient client;
            private readonly NeoWritePlan plan;

            internal PlanGraph(NeoClient client, NeoWritePlan plan)
            {
                this.client = client;
                this.plan = plan;
            }

            public MemberValueBase? Row(string valueId) => plan.Resolve(valueId);

            public IEnumerable<string> UnorderedEntryIds(string containerId)
            {
                foreach (string id in plan.ContainerCandidates(containerId))
                    yield return id;
                if (!plan.TryGetOwnership(containerId, out NeoValueOwnership ownership) || ownership == NeoValueOwnership.Asset)
                    yield break;
                foreach (string id in client.EnumerateContainerMemberValueIds(ownership, containerId))
                    if (!plan.WritesRow(id))
                        yield return id;
            }

            internal void Apply(NeoTypeSourceRequirement placed)
            {
                if (!Changes(placed))
                    return;
                var row = (MemberValue)placed.Row;
                if (!plan.TryGetOwnership(row.id, out NeoValueOwnership ownership))
                    throw new InvalidOperationException($"Placed row \"{row.id}\" has no store.");
                if (!plan.Rows.TryGetValue((ownership, row.id), out MemberValue? staged) || !ReferenceEquals(staged, row))
                {
                    row = client.CloneRowForWrite(row);
                    plan.Set(ownership, row, silent: true);
                }
                NeoTypeSources.Apply(
                    new NeoTypeSourceRequirement(
                        row,
                        placed.Position,
                        placed.StampParams,
                        placed.SpelledStamp,
                        placed.TypeSource,
                        placed.BindingClassId),
                    Row);
            }
        }

        // ------------------------------------------------------------------
        // Sourced types (type-sources.ts).
        // ------------------------------------------------------------------

        /// <summary>
        /// Only binding nodes store a stamp: a class row in an <c>object</c>,
        /// interface or type-parameter slot, in a slot that admits a narrower
        /// construction, or with no slot.
        /// </summary>
        private static bool IsBindingNodePlacement(NeoClient client, Member? placement)
        {
            switch (placement)
            {
                case null:
                case ObjectMember:
                case GenericMember:
                    return true;
                case ClassMember classMember:
                    return classMember.DeclaredInterfaceId is not null || AdmitsNarrowedConstruction(client, classMember);
                default:
                    return false;
            }
        }

        private static bool AdmitsNarrowedConstruction(NeoClient client, Member slot)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Member? current = slot;
            while (current is not null && !string.IsNullOrEmpty(current.id) && visited.Add(current.id))
            {
                if (current.id is AnimationChildOverridesMemberId or AnimationChildOverridesEntryMemberId)
                    return true;
                if (string.IsNullOrEmpty(current.extendsMemberId) || !client.TryGetMember(current.extendsMemberId!, out current))
                    return false;
            }
            return false;
        }

        /// <summary>The source's binding at <paramref name="path"/>, as a sourced type.</summary>
        private static TypeBinding SourcePathType(IReadOnlyList<string> path) =>
            TypeBinding.Open(SourceOwner, JsonConvert.SerializeObject(path));

        private static bool IsSourceOwned(TypeBinding type) =>
            type.Kind == TypeBindingKind.Open
            && type.Definition!.Type == MemberKind.Class
            && type.Definition.ClassId == SourceOwnerClassId;

        private static string[]? SourcePathOf(TypeBinding type)
        {
            if (!IsSourceOwned(type) || type.Param == DependentParam)
                return null;
            return JsonConvert.DeserializeObject<string[]>(type.Param!)
                ?? throw new InvalidOperationException($"Sourced path '{type.Param}' is not a key list.");
        }

        /// <summary>
        /// The environment a class row reads its members in: each own param
        /// from <paramref name="ownArgument"/>, each base param from the
        /// chain's <c>extendsGenericBindings</c>. A param left out stays out.
        /// </summary>
        private static Dictionary<string, TypeBinding> ClassSourcedEnv(
            NeoClient client,
            string classId,
            Func<string, TypeBinding?> ownArgument)
        {
            IList<NeoSchemaClass> chain = client.ResolveClassInheritanceChain(classId);
            var env = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            if (chain.Count == 0)
                return env;
            if (chain[0].genericParams is { } own)
                foreach (GenericParamDeclaration param in own)
                    if (ownArgument(param.id) is TypeBinding argument)
                        env[param.id] = argument;
            for (int level = 1; level < chain.Count; level++)
            {
                NeoSchemaClass child = chain[level - 1];
                if (chain[level].genericParams is not { } inherited)
                    continue;
                foreach (GenericParamDeclaration param in inherited)
                {
                    if (child.extendsGenericBindings is null
                        || !child.extendsGenericBindings.TryGetValue(param.id, out TypeBinding? binding))
                    {
                        continue;
                    }
                    env[param.id] = Substitute(binding, open => env.TryGetValue(open.Param!, out TypeBinding? bound) ? bound : null);
                }
            }
            return env;
        }

        /// <summary>A class row's environment in a slot typed <paramref name="position"/>, read against the source.</summary>
        private static Dictionary<string, TypeBinding> RowSourcedEnv(
            NeoClient client,
            string classId,
            TypeBinding position,
            string? positionClassId,
            Func<string, TypeBinding?> realArgument,
            string? templateClassId)
        {
            if (!NamesSource(position, templateClassId))
                return ClassSourcedEnv(client, classId, realArgument);
            bool sameClass = positionClassId == classId;
            string[]? path = SourcePathOf(position);
            if (sameClass && path is not null)
                return ClassSourcedEnv(client, classId, paramId => SourcePathType(path.Append(paramId).ToArray()));
            if (sameClass && position.Kind == TypeBindingKind.Closed && position.Head!.Type == MemberKind.Class)
            {
                IReadOnlyDictionary<string, TypeBinding>? bindings = position.Bindings;
                return ClassSourcedEnv(client, classId, paramId =>
                    bindings is not null && bindings.TryGetValue(paramId, out TypeBinding? bound) && bound.Kind != TypeBindingKind.Definition
                        ? bound
                        : null);
            }
            return ClassSourcedEnv(client, classId, _ => SourceDependent);
        }

        /// <summary>The type <paramref name="member"/> declares, read against the source.</summary>
        private static TypeBinding SourcedMemberType(NeoClient client, Member member, IReadOnlyDictionary<string, TypeBinding> env) =>
            ArgumentOfMember(client, member, paramId =>
                env.TryGetValue(paramId, out TypeBinding? bound)
                    ? bound
                    : throw new InvalidOperationException($"Member '{member.id}' names param '{paramId}', which its placement leaves unbound."));

        /// <summary>
        /// The type <paramref name="member"/> declares, each param it names
        /// open under the <c>$declared</c> owner: whether a row needs
        /// typeSource, and what a container reader matches its type against.
        /// </summary>
        private static TypeBinding DeclaredMemberType(NeoClient client, Member member) =>
            ArgumentOfMember(client, member, paramId => TypeBinding.Open(DeclaredOwner, paramId));

        /// <summary>The type at <paramref name="key"/> of a container typed <paramref name="container"/>.</summary>
        private static TypeBinding SourcedEntryType(TypeBinding container, string key)
        {
            if (IsSourceOwned(container))
            {
                string[]? path = SourcePathOf(container);
                return path is null ? SourceDependent : SourcePathType(path.Append(key).ToArray());
            }
            if (container.Kind != TypeBindingKind.Closed)
                throw new InvalidOperationException($"A container typed by param '{container.Param}' has no entry type.");
            if (container.Bindings is null || !container.Bindings.TryGetValue(key, out TypeBinding? entry))
                throw new InvalidOperationException($"A {container.Head!.Type} container binds no position '{key}'.");
            if (entry.Kind == TypeBindingKind.Definition)
                throw new InvalidOperationException($"A {container.Head!.Type} container holds a generic definition at '{key}'.");
            return entry;
        }

        /// <summary>
        /// The typeSource a row that needs one stores (§12.1): the
        /// binding-key path when its type is a binding the source holds, and
        /// otherwise its own type over the source's params, naming the source
        /// exactly when it stays open. A template row's type stays open over
        /// its owning class's params, with no row.
        /// </summary>
        private static NeoTypeSource TypeSourceOf(
            NeoTypeSourceOrigin? origin,
            TypeBinding type,
            TypeBinding concrete,
            string? templateClassId)
        {
            string[]? exact = SourcePathOf(type);
            if (exact is { Length: > 0 })
            {
                if (origin is null)
                    throw new InvalidOperationException("A row's type is a source binding, but no binding node or box row owns it.");
                return NeoTypeSource.AtPath(origin.ValueId, exact);
            }
            TypeBinding own = origin is null || !NamesSource(type, templateClassId)
                ? ClosedType(type, concrete, templateClassId)
                : OverSourceParams(type, origin, concrete);
            return origin is not null && HasOpenArm(own)
                ? NeoTypeSource.OfType(own, origin.ValueId)
                : NeoTypeSource.OfType(own);
        }

        private static TypeBinding ClosedType(TypeBinding type, TypeBinding concrete, string? templateClassId)
        {
            if (NamesSource(type, templateClassId))
                throw new InvalidOperationException("A row's type names a source binding, but no binding node or box row owns it.");
            if (templateClassId is null && !IsFullyClosed(concrete))
                throw new InvalidOperationException("A row whose declarations close its type has an open type.");
            return concrete;
        }

        /// <summary>Whether a position's type reads anything from its source.</summary>
        private static bool NamesSourceBinding(TypeBinding type, string? templateClassId) =>
            SourcePathOf(type) is not null || NamesSource(type, templateClassId);

        /// <summary>
        /// A sourced type over the source's own params: each one-key source
        /// path is <c>Open(sourceClass, param)</c>. A type naming any other
        /// source position stores its closed type, which needs no hop.
        /// </summary>
        private static TypeBinding OverSourceParams(TypeBinding type, NeoTypeSourceOrigin origin, TypeBinding concrete)
        {
            bool foreign = false;
            TypeBinding mapped = Substitute(type, open =>
            {
                if (!IsSourceOwned(open))
                    return null;
                string[]? path = SourcePathOf(open);
                if (origin.ClassId is null || path is null || path.Length != 1)
                {
                    foreign = true;
                    return null;
                }
                return TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: origin.ClassId), path[0]);
            });
            if (!foreign)
                return mapped;
            if (!IsFullyClosed(concrete))
                throw new InvalidOperationException($"A container behind a class slot names source \"{origin.ValueId}\" at no param, and its closed type is unknown.");
            return concrete;
        }

        private static bool NamesSource(TypeBinding type, string? templateClassId)
        {
            if (type.Kind == TypeBindingKind.Open)
            {
                if (IsSourceOwned(type))
                    return true;
                if (templateClassId is not null && type.Definition!.ClassId == templateClassId)
                    return false;
                throw new InvalidOperationException($"A placed row's type names param '{type.Param}', which no owner binds.");
            }
            if (type.Bindings is null)
                return false;
            foreach (TypeBinding inner in type.Bindings.Values)
                if (inner.Kind != TypeBindingKind.Definition && NamesSource(inner, templateClassId))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Member types (type-binding-views.ts typeArgumentOfMember).
        // ------------------------------------------------------------------

        /// <summary>
        /// Whether a type spells <paramref name="member"/>. A function
        /// reference, a callable, a Lookup bound to a collection or a Lookup
        /// of a Lookup selection has none, nor does a container of one.
        /// </summary>
        private static bool HasTypeBinding(NeoClient client, Member member)
        {
            switch (member.kind)
            {
                case MemberKind.Null:
                case MemberKind.FunctionRef:
                case MemberKind.NSFunction:
                case MemberKind.Function:
                case MemberKind.NSProperty:
                case MemberKind.Variant:
                    return false;
            }
            bool Inner(string id) => client.TryGetMember(id, out Member? found) && HasTypeBinding(client, found);
            return member switch
            {
                // No type binding spells a Lookup of a Lookup selection.
                LookupMember lookup => lookup.collectionMemberId is null && lookup.declaredTypeInfo is not LookupTypeInfo,
                ListMember list => Inner(list.entryMemberId),
                DictionaryMember dictionary => Inner(dictionary.entryMemberId),
                TupleMember tuple => tuple.elementMemberIds.All(Inner),
                _ => true,
            };
        }

        /// <summary>
        /// The type argument <paramref name="member"/> declares, each param it
        /// names read through <paramref name="param"/>.
        /// </summary>
        internal static TypeBinding ArgumentOfMember(NeoClient client, Member member, Func<string, TypeBinding> param)
        {
            bool required = member.Requirement == NeoMemberRequirementKind.Required;
            TypeBinding Of(string id, string label)
            {
                if (!client.TryGetMember(id, out Member? inner))
                    throw new InvalidOperationException($"Member '{member.id}' names missing {label} '{id}'.");
                return ArgumentOfMember(client, inner, param);
            }
            TypeBinding FromTypeInfo(TypeInfo typeInfo) =>
                Substitute(NeoTypeBindings.ArgumentOf(NeoTypeRules.Declared(typeInfo)), open => param(open.Param!));
            TypeBinding SetOf(TypeBinding element) => Positional(new TypeHead(MemberKind.Set, required), element);
            switch (member)
            {
                case GenericMember generic:
                    return param(generic.genericParamId);
                case ClassMember classMember:
                    {
                        TypeHead head = classMember.DeclaredInterfaceId is string interfaceId
                            ? new TypeHead(MemberKind.Interface, required, interfaceId: interfaceId)
                            : new TypeHead(MemberKind.Class, required, classId: classMember.DeclaredClassId);
                        Dictionary<string, TypeBinding>? arguments = null;
                        if (classMember.TypeArguments is { } declared)
                        {
                            arguments = new Dictionary<string, TypeBinding>(declared.Count, StringComparer.Ordinal);
                            foreach (KeyValuePair<string, TypeBinding> pair in declared)
                                arguments[pair.Key] = pair.Value.Kind == TypeBindingKind.Definition
                                    ? pair.Value
                                    : Substitute(pair.Value, open => param(open.Param!));
                        }
                        return TypeBinding.Closed(head, arguments);
                    }
                case EnumMember enumMember:
                    {
                        bool multi = enumMember.Selection == NeoMemberSelectionKind.Multi;
                        TypeBinding option = TypeBinding.Closed(new TypeHead(MemberKind.Enum, multi || required, enumId: enumMember.enumId));
                        return multi ? SetOf(option) : option;
                    }
                case DialogueLookupMember dialogue:
                    return dialogue.Selection == NeoMemberSelectionKind.Multi
                        ? SetOf(TypeBinding.Closed(new TypeHead(MemberKind.DialogueLookup, true)))
                        : TypeBinding.Closed(new TypeHead(MemberKind.DialogueLookup, required));
                case LookupMember lookup:
                    {
                        if (lookup.collectionMemberId is not null)
                            throw new InvalidOperationException($"Member '{member.id}' is a Lookup bound to a collection, which no type represents.");
                        TypeBinding target = FromTypeInfo(lookup.declaredTypeInfo
                            ?? throw new InvalidOperationException($"Member '{member.id}' is a Lookup with no declared type."));
                        return lookup.Selection == NeoMemberSelectionKind.Multi
                            ? SetOf(Positional(new TypeHead(MemberKind.Reference, true), target))
                            : Positional(new TypeHead(MemberKind.Reference, required), target);
                    }
                case SetMember set:
                    return SetOf(Of(set.entryMemberId, "entry member"));
                case ListMember list:
                    return Positional(new TypeHead(MemberKind.List, required), Of(list.entryMemberId, "entry member"));
                case DictionaryMember dictionary:
                    {
                        TypeBinding key = dictionary.keyEnumId is string keyEnumId
                            ? TypeBinding.Closed(new TypeHead(MemberKind.Enum, true, enumId: keyEnumId))
                            : TypeBinding.Closed(new TypeHead(MemberKind.String, true));
                        return Positional(new TypeHead(MemberKind.Dictionary, required), key, Of(dictionary.entryMemberId, "entry member"));
                    }
                case TupleMember tuple:
                    return Positional(
                        new TypeHead(MemberKind.Tuple, required),
                        tuple.elementMemberIds.Select(id => Of(id, "tuple element")).ToArray());
                case DelegateMember delegateMember:
                    return FromTypeInfo(new DelegateTypeInfo
                    {
                        type = MemberKind.NSDelegate,
                        required = required,
                        returnTypeInfo = delegateMember.returnTypeInfo,
                        argumentTypes = delegateMember.argumentTypes,
                    });
                case ActionMember actionMember:
                    return FromTypeInfo(new ActionTypeInfo
                    {
                        type = MemberKind.NSAction,
                        required = required,
                        argumentTypes = actionMember.argumentTypes,
                    });
                case ObjectMember objectMember:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, required, objectType: objectMember.objectType));
            }
            switch (member.kind)
            {
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.String:
                case MemberKind.Decimal:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                    return TypeBinding.Closed(new TypeHead(member.kind, required));
                default:
                    throw new InvalidOperationException($"Member '{member.id}' of kind {member.kind} has no type binding.");
            }
        }

        private static TypeBinding Positional(TypeHead head, params TypeBinding[] positions)
        {
            if (positions.Length == 0)
                return TypeBinding.Closed(head);
            var bindings = new Dictionary<string, TypeBinding>(positions.Length, StringComparer.Ordinal);
            for (int index = 0; index < positions.Length; index++)
                bindings[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = positions[index];
            return TypeBinding.Closed(head, bindings);
        }

        // ------------------------------------------------------------------
        // Binding helpers.
        // ------------------------------------------------------------------

        /// <summary><paramref name="binding"/> with each open arm <paramref name="resolve"/> answers replaced.</summary>
        internal static TypeBinding Substitute(TypeBinding binding, Func<TypeBinding, TypeBinding?> resolve)
        {
            if (binding.Kind == TypeBindingKind.Open)
                return resolve(binding) ?? binding;
            if (binding.Kind != TypeBindingKind.Closed || binding.Bindings is null)
                return binding;
            Dictionary<string, TypeBinding>? bindings = null;
            foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
            {
                if (pair.Value.Kind == TypeBindingKind.Definition)
                    continue;
                TypeBinding next = Substitute(pair.Value, resolve);
                if (ReferenceEquals(next, pair.Value))
                    continue;
                bindings ??= new Dictionary<string, TypeBinding>(binding.Bindings, StringComparer.Ordinal);
                bindings[pair.Key] = next;
            }
            return bindings is null ? binding : TypeBinding.Closed(binding.Head!, bindings);
        }

        /// <summary>Whether <paramref name="binding"/> names no type parameter at any depth.</summary>
        internal static bool IsFullyClosed(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return false;
            if (binding.Bindings is not null)
                foreach (TypeBinding inner in binding.Bindings.Values)
                    if (inner.Kind != TypeBindingKind.Definition && !IsFullyClosed(inner))
                        return false;
            return true;
        }

        /// <summary>Whether <paramref name="binding"/> has an open arm at any depth.</summary>
        internal static bool HasOpenArm(TypeBinding binding)
        {
            if (binding.Kind == TypeBindingKind.Open)
                return true;
            if (binding.Bindings is not null)
                foreach (TypeBinding inner in binding.Bindings.Values)
                    if (HasOpenArm(inner))
                        return true;
            return false;
        }
    }
}
