// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        internal enum DetachedSlotKind : byte
        {
            Leaf,
            Class,
            List,
        }

        /// <summary>One stored member of a <see cref="DetachedClassPlan"/>.</summary>
        internal sealed class DetachedSlot
        {
            internal string schemaKey = null!;
            internal string memberId = null!;
            internal Member member = null!;
            internal DetachedSlotKind kind;
            internal InitializerBody? initializer;
            internal bool hasLiteralDefault;
            /// <summary>List entry member and kind; null for other slots.</summary>
            internal Member? entryMember;
            internal DetachedSlotKind entryKind;
        }

        /// <summary>
        /// The slot layout a <see cref="NeoScriptObject"/> of one class uses.
        /// Built once per class and cleared with the constructor schema caches.
        /// </summary>
        internal sealed class DetachedClassPlan
        {
            internal string classId = null!;
            internal ClassTypeInfo classTypeInfo = null!;
            internal RuntimeClassPlan runtimePlan = null!;
            internal DetachedSlot[] slots = null!;
            internal Dictionary<string, int> slotByKey = null!;
            /// <summary>The class declares a required constructor, so only a declared construction may build it.</summary>
            internal bool requiresConstructor;
        }

        /// <summary>
        /// The detached plan for <paramref name="classId"/>, or null when an
        /// instance must be built as rows from the start. Only closed,
        /// non-generic classes whose stored members are scalars, enums, full
        /// Class values, or ordered lists of those qualify; everything else
        /// keeps the eager construction path.
        /// </summary>
        internal static DetachedClassPlan? ResolveDetachedClassPlan(
            NeoClient client,
            string classId)
        {
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(client);
            lock (cache.gate)
            {
                if (cache.detachedPlans.TryGetValue(classId, out DetachedClassPlan? cached))
                    return cached;
            }
            DetachedClassPlan? plan = BuildDetachedClassPlan(client, classId);
            lock (cache.gate)
            {
                cache.detachedPlans[classId] = plan;
            }
            return plan;
        }

        private static DetachedClassPlan? BuildDetachedClassPlan(
            NeoClient client,
            string classId)
        {
            if (!client.TryGetClass(classId, out NeoSchemaClass? schemaClass)
                || schemaClass!.Modifier == NeoClassModifierKind.Abstract
                || client.TryResolveSchemaClassAllowedOwnership(classId, out NeoValueOwnership allowed)
                    && allowed == NeoValueOwnership.Asset)
            {
                return null;
            }
            RuntimeClassPlan runtimePlan;
            try
            {
                runtimePlan = ResolveRuntimeClassPlan(client, classId);
            }
            catch (Exception error) when (error is InvalidOperationException or CircularInheritanceError)
            {
                return null;
            }
            if (runtimePlan.genericEnv.Count != 0)
                return null;
            var slots = new List<DetachedSlot>(runtimePlan.schema.Count);
            foreach (MergedSchemaEntry entry in runtimePlan.schema)
            {
                Member member = runtimePlan.membersBySchemaKey[entry.schemaKey];
                if (!IsStoredConstructorMember(member))
                    continue;
                if (client.ChildOwnership(member, NeoValueOwnership.Session) != NeoValueOwnership.Session)
                    return null;
                InitializerBody? initializer = InitializerOf(member);
                var slot = new DetachedSlot
                {
                    schemaKey = entry.schemaKey,
                    memberId = entry.memberId,
                    member = member,
                    initializer = initializer,
                    hasLiteralDefault = initializer is null && HasExplicitDefaultValue(member),
                };
                if (!TryClassifyDetachedMember(client, member, out slot.kind)
                    || initializer is not null && slot.kind != DetachedSlotKind.Leaf)
                {
                    // A computed Class or List default would build rows of its
                    // own; only leaf initializers evaluate into a slot.
                    return null;
                }
                switch (member)
                {
                    case ClassMember classMember
                        when slot.hasLiteralDefault && classMember.defaultValue!.value is not null:
                        // An authored default graph would have to be built as
                        // rows anyway; a null default reads as null.
                        return null;
                    case ListMember listMember:
                        if (slot.hasLiteralDefault && listMember.defaultValue!.value is { Length: > 0 })
                            return null;
                        if (!client.TryGetMember(listMember.entryMemberId, out Member? entryMember)
                            || entryMember is ListMember
                            || !TryClassifyDetachedMember(client, entryMember, out slot.entryKind))
                        {
                            return null;
                        }
                        slot.entryMember = entryMember;
                        break;
                }
                slots.Add(slot);
            }
            var slotByKey = new Dictionary<string, int>(slots.Count, StringComparer.Ordinal);
            for (int index = 0; index < slots.Count; index++)
                slotByKey.Add(slots[index].schemaKey, index);
            return new DetachedClassPlan
            {
                classId = classId,
                classTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = classId,
                },
                runtimePlan = runtimePlan,
                slots = slots.ToArray(),
                slotByKey = slotByKey,
                requiresConstructor = !string.IsNullOrEmpty(schemaClass.requiredConstructorId),
            };
        }

        private static bool TryClassifyDetachedMember(
            NeoClient client,
            Member member,
            out DetachedSlotKind kind)
        {
            switch (member)
            {
                case BoolMember:
                case IntMember:
                case FloatMember:
                case StringMember:
                case Vector2Member:
                case Vector2IntMember:
                case Vector3Member:
                case Vector3IntMember:
                case ColorMember:
                case EnumMember enumMember
                    when enumMember.enumId != NeoCellPatternStorage.ExcludingEnumId:
                    kind = DetachedSlotKind.Leaf;
                    return true;
                case ClassMember classMember
                    when classMember.classArguments is null
                        && classMember.Payload == NeoMemberPayloadKind.Full:
                    kind = DetachedSlotKind.Class;
                    return true;
                case ListMember listMember when !client.IsUnorderedList(listMember):
                    kind = DetachedSlotKind.List;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        /// <summary>
        /// Builds a detached instance from already-evaluated constructor
        /// fields, or returns null when a field value cannot live in a slot
        /// (the caller then constructs rows from the same fields). Leaf
        /// initializers of unsupplied members run here, in schema order,
        /// exactly as the row path runs them.
        /// </summary>
        internal static NeoScriptObject? CreateDetached(
            DetachedClassPlan plan,
            IReadOnlyList<RuntimeConstructorField> fields,
            NSGetterEvaluator.Context constructionCtx,
            IReadOnlyDictionary<string, object?[]>? initializerArguments = null,
            string? constructedClassId = null)
        {
            var created = new NeoScriptObject(
                constructionCtx.client,
                plan,
                constructionCtx.allocationTracker);
            foreach (RuntimeConstructorField field in fields)
            {
                if (!plan.slotByKey.TryGetValue(field.schemaKey, out int index)
                    || field.value is null
                        && RequiresRuntimeConstructorArgument(plan.slots[index].member)
                    || field.value is not null
                        && !TryStoreDetachedSlot(created, index, field.value, constructionCtx))
                {
                    ReleaseDetachedChildren(created);
                    return null;
                }
            }
            NeoConstructionScope? scope = null;
            for (int index = 0; index < plan.slots.Length; index++)
            {
                DetachedSlot slot = plan.slots[index];
                if (slot.initializer is null
                    || created.states[index] != NeoScriptObject.DefaultSlot)
                {
                    continue;
                }
                scope ??= new NeoConstructionScope(
                    constructionCtx.client,
                    constructionCtx,
                    initializerArguments,
                    constructedClassId);
                object? produced = scope.EvaluateInitializer(slot.member, slot.initializer);
                if (!TryStoreDetachedSlot(created, index, produced, constructionCtx))
                {
                    throw new InvalidOperationException(produced is null
                        ? $"Required constructor field '{slot.member.name}' received null."
                        : $"Cannot set {slot.member.GetType().Name} {slot.member.id} from {produced.GetType().Name}");
                }
            }
            return created;
        }

        /// <summary>
        /// A declared construction as a detached instance, in
        /// <see cref="ConstructDeclaredClassValueData"/>'s order: member
        /// initializers, the base chain and body against a detached
        /// <c>this</c>, then the call-site fields evaluated and applied, then
        /// the completeness check and the creation recipe. Anything that needs
        /// a row mid-construction (a write the slots cannot take, <c>this</c>
        /// escaping) materializes the instance, and the steps after it write
        /// through the row path exactly as the eager path does.
        /// </summary>
        internal static NeoScriptObject ConstructDetachedDeclared(
            NeoResolvedDeclaredConstructor resolved,
            DetachedClassPlan plan,
            IReadOnlyDictionary<string, object?> argumentValues,
            IReadOnlyList<RuntimeConstructorField> fields,
            NSGetterEvaluator.Context ctx,
            Action<NSGetterEvaluator.Context>? evaluateFieldValues)
        {
            NeoClient client = resolved.client;
            NSGetterEvaluator.Context constructionCtx =
                PushConstructionFrame(ctx, resolved.schemaClass.name);
            object?[] positionalArguments = OrderDeclaredArguments(
                resolved.link.record,
                argumentValues);
            Dictionary<string, object?[]> initializerArguments = PrepareConstructorInitializerArguments(
                client,
                resolved.link,
                positionalArguments,
                constructionCtx);
            NeoScriptObject created = CreateDetached(
                plan,
                Array.Empty<RuntimeConstructorField>(),
                constructionCtx,
                initializerArguments,
                plan.classId)!;
            created.constructing = true;
            try
            {
                RunDeclaredConstructorChain(
                    client,
                    resolved,
                    resolved.link,
                    positionalArguments,
                    created,
                    null,
                    constructionCtx,
                    initializerArguments);
                evaluateFieldValues?.Invoke(constructionCtx);
                ApplyDetachedConstructorFields(resolved, created, fields, constructionCtx);
                if (created.attachedId is null)
                    AssertDetachedRootIsComplete(resolved, created);
                else
                    AssertDeclaredConstructorRootIsComplete(client, resolved, created.attachedId);
                // Recording a row-typed argument may attach it, and with it
                // this instance when the body adopted it.
                created.constructorArgs = BuildConstructionProvenanceArgs(
                    client,
                    resolved,
                    argumentValues,
                    constructionCtx,
                    deferLiterals: true,
                    out created.constructorLiterals);
                created.constructorId = resolved.link.record?.id;
                if (created.attachedId is not null
                    && client.TryGetValue(NeoValueOwnership.Session, created.attachedId, out ObjectMemberValue? live))
                {
                    StampDetachedProvenance(created, live!);
                }
            }
            catch
            {
                if (created.attachedId is string attached)
                    ReclaimFailedConstruction(client, attached, constructionCtx);
                else
                    ReleaseDetachedChildren(created);
                throw;
            }
            finally
            {
                created.constructing = false;
            }
            return created;
        }

        /// <summary>
        /// Applies constructor fields to a detached <c>this</c>: each field
        /// that can stay in its slot does; the first that cannot materializes
        /// the instance, and it and the rest go through the row path.
        /// </summary>
        private static void ApplyDetachedConstructorFields(
            NeoResolvedDeclaredConstructor resolved,
            NeoScriptObject target,
            IReadOnlyList<RuntimeConstructorField> fields,
            NSGetterEvaluator.Context ctx)
        {
            for (int field = 0; field < fields.Count; field++)
            {
                RuntimeConstructorField supplied = fields[field];
                if (target.attachedId is null)
                {
                    if (supplied.value is null
                        && resolved.membersBySchemaKey[supplied.schemaKey].Requirement == NeoMemberRequirementKind.Required)
                    {
                        throw new InvalidOperationException(
                            $"Constructor field '{supplied.schemaKey}' on '{resolved.schemaClass.name}' is required and cannot be null.");
                    }
                    if (target.plan.slotByKey.TryGetValue(supplied.schemaKey, out int index)
                        && TryStoreDetachedSlot(target, index, supplied.value, ctx))
                    {
                        continue;
                    }
                }
                var remaining = new List<RuntimeConstructorField>(fields.Count - field);
                for (int rest = field; rest < fields.Count; rest++)
                    remaining.Add(fields[rest]);
                ApplyDeclaredConstructorFields(
                    resolved.client,
                    resolved,
                    NSGetterEvaluator.AttachDetached(target, ctx),
                    remaining,
                    ctx);
                return;
            }
        }

        private static void AssertDetachedRootIsComplete(
            NeoResolvedDeclaredConstructor resolved,
            NeoScriptObject created)
        {
            DetachedSlot[] slots = created.plan.slots;
            for (int index = 0; index < slots.Length; index++)
            {
                DetachedSlot slot = slots[index];
                if (created.states[index] != NeoScriptObject.DefaultSlot
                    || slot.hasLiteralDefault
                    || slot.member.Requirement != NeoMemberRequirementKind.Required)
                {
                    continue;
                }
                throw new InvalidOperationException(
                    $"Declared constructor for '{resolved.schemaClass.name}' left required member '{slot.schemaKey}'/'{slot.memberId}' unset. Assign it in the constructor body, give it a default, or pass it at the call site.");
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> into a slot when it can stay
        /// detached: a leaf in the slot's stored shape, an unowned detached
        /// object of an assignable class, or a local list of those. Anything
        /// else returns false so the caller takes the row path, which owns
        /// every other conversion and error.
        /// </summary>
        internal static bool TryStoreDetachedSlot(
            NeoScriptObject target,
            int index,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            DetachedSlot slot = target.plan.slots[index];
            object? stored;
            if (value is null)
            {
                if (slot.member.Requirement == NeoMemberRequirementKind.Required)
                    return false;
                stored = null;
            }
            else if (slot.kind == DetachedSlotKind.List)
            {
                if (value is not object?[] entries
                    || !NSGetterEvaluator.IsLocalCollection(entries, ctx))
                {
                    return false;
                }
                var copy = new object?[entries.Length];
                for (int entry = 0; entry < entries.Length; entry++)
                {
                    if (!TryNormalizeDetachedValue(
                            target,
                            slot.entryMember!,
                            slot.entryKind,
                            entries[entry],
                            out copy[entry]))
                    {
                        return false;
                    }
                }
                if (!TryAdoptDetachedEntries(target, copy))
                    return false;
                ReleaseDetachedSlot(target, index);
                SetDetachedListEntries(target, index, copy);
                target.states[index] = NeoScriptObject.WrittenSlot;
                return true;
            }
            else if (!TryNormalizeDetachedValue(target, slot.member, slot.kind, value, out stored)
                || stored is NeoScriptObject child && !TryAdoptDetachedChild(target, child))
            {
                return false;
            }
            ReleaseDetachedSlot(target, index);
            target.values[index] = stored;
            target.states[index] = NeoScriptObject.WrittenSlot;
            return true;
        }

        /// <summary>
        /// Appends one entry to a List slot. The slot's entries move into a
        /// growable buffer on the first append, so a loop of appends is linear.
        /// </summary>
        internal static bool TryAddDetachedListEntry(
            NeoScriptObject target,
            int index,
            object? entry)
        {
            DetachedSlot slot = target.plan.slots[index];
            if (slot.kind != DetachedSlotKind.List
                || target.states[index] == NeoScriptObject.DefaultSlot && !slot.hasLiteralDefault
                || !TryNormalizeDetachedValue(target, slot.entryMember!, slot.entryKind, entry, out object? stored)
                || stored is NeoScriptObject child && !TryAdoptDetachedChild(target, child))
            {
                return false;
            }
            target.listBuffers ??= new List<object?>?[target.plan.slots.Length];
            List<object?>? buffer = target.listBuffers[index];
            if (buffer is null)
            {
                buffer = target.values[index] is object?[] entries
                    ? new List<object?>(entries)
                    : new List<object?>();
                target.listBuffers[index] = buffer;
            }
            buffer.Add(stored);
            target.values[index] = null;
            target.states[index] = NeoScriptObject.WrittenSlot;
            return true;
        }

        /// <summary>Where a List slot's entry array came from, so an alias of it reads the slot's current entries.</summary>
        internal sealed class DetachedListOrigin
        {
            internal readonly NeoScriptObject owner;
            internal readonly int index;

            internal DetachedListOrigin(NeoScriptObject owner, int index)
            {
                this.owner = owner;
                this.index = index;
            }
        }

        private static readonly ConditionalWeakTable<object?[], DetachedListOrigin> DetachedListOrigins = new();

        internal static bool TryGetDetachedListOrigin(object?[] entries, out DetachedListOrigin? origin) =>
            DetachedListOrigins.TryGetValue(entries, out origin);

        /// <summary>Stores a List slot's entry array and records its origin.</summary>
        internal static object?[] SetDetachedListEntries(NeoScriptObject target, int index, object?[] entries)
        {
            target.values[index] = entries;
            DetachedListOrigins.AddOrUpdate(entries, new DetachedListOrigin(target, index));
            return entries;
        }

        /// <summary>The current entries of a List slot: its array, rebuilt after appends.</summary>
        internal static object?[]? DetachedListEntries(NeoScriptObject target, int index)
        {
            if (target.values[index] is object?[] entries)
                return entries;
            if (target.listBuffers?[index] is not List<object?> buffer)
                return null;
            return SetDetachedListEntries(target, index, buffer.ToArray());
        }

        private static bool TryNormalizeDetachedValue(
            NeoScriptObject target,
            Member member,
            DetachedSlotKind kind,
            object? value,
            out object? stored)
        {
            stored = null;
            if (value is null)
                return member.Requirement != NeoMemberRequirementKind.Required;
            switch (kind)
            {
                case DetachedSlotKind.Class:
                    // A constructor still running keeps its own root.
                    if (value is not NeoScriptObject { attachedId: null, owner: null, constructing: false } child
                        || child == target
                        || child.plan.classId != ((ClassMember)member).classId
                            && !IsAssignableNeoSchemaClass(
                                target.client,
                                child.plan.classId,
                                ((ClassMember)member).classId))
                    {
                        return false;
                    }
                    stored = child;
                    return true;
                case DetachedSlotKind.Leaf:
                    return TryNormalizeDetachedLeaf(target, member, value, out stored);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The value a leaf row built from <paramref name="value"/> reads back
        /// as: numbers as double, strings verbatim (runtime writes are literal),
        /// enums as a fresh option-id array, vectors and colors as a copy owned
        /// by <paramref name="target"/>, as a row copies the written payload.
        /// Ints must be integral, as the row's shape check requires.
        /// </summary>
        private static bool TryNormalizeDetachedLeaf(
            NeoScriptObject target,
            Member member,
            object value,
            out object? stored)
        {
            stored = null;
            switch (member)
            {
                case Vector3Member when value is NeoVector3Value vector3:
                case Vector3IntMember when value is NeoVector3Value intVector3
                    && NeoVectorValues.IsInt(intVector3.x)
                    && NeoVectorValues.IsInt(intVector3.y)
                    && NeoVectorValues.IsInt(intVector3.z):
                    NeoVector3Value vector3Copy = CloneVector3((NeoVector3Value)value)!;
                    vector3Copy.detachedOwner = target;
                    stored = vector3Copy;
                    return true;
                case Vector2Member when value is NeoVector2Value:
                case Vector2IntMember when value is NeoVector2Value vector2
                    && NeoVectorValues.IsInt(vector2.x)
                    && NeoVectorValues.IsInt(vector2.y):
                    NeoVector2Value vector2Copy = CloneVector2((NeoVector2Value)value)!;
                    vector2Copy.detachedOwner = target;
                    stored = vector2Copy;
                    return true;
                case ColorMember when value is NeoColorValue color:
                    NeoColorValue colorCopy = CloneColor(color)!;
                    colorCopy.detachedOwner = target;
                    stored = colorCopy;
                    return true;
                case BoolMember when value is bool:
                case StringMember when value is string:
                    stored = value;
                    return true;
                case FloatMember when value is double:
                    stored = value;
                    return true;
                case IntMember when NeoScriptValueMarshaller.IsIntegralNumber(value):
                case FloatMember when value is int or float:
                    stored = Convert.ToDouble(value);
                    return true;
                case EnumMember enumMember when value is object?[] options:
                    if (options.Length == 0 && enumMember.Selection != NeoMemberSelectionKind.Multi)
                        return false;
                    if (options.Length > 1 && enumMember.Selection != NeoMemberSelectionKind.Multi)
                        return false;
                    var copy = new object?[options.Length];
                    for (int index = 0; index < options.Length; index++)
                    {
                        if (options[index] is not string)
                            return false;
                        copy[index] = options[index];
                    }
                    stored = copy;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Makes <paramref name="target"/> own <paramref name="child"/> unless
        /// that would close a cycle, which the row path reports.
        /// </summary>
        private static bool TryAdoptDetachedChild(NeoScriptObject target, NeoScriptObject child)
        {
            for (NeoScriptObject? cursor = target; cursor is not null; cursor = cursor.owner)
            {
                if (cursor == child)
                    return false;
            }
            child.owner = target;
            return true;
        }

        private static bool TryAdoptDetachedEntries(NeoScriptObject target, object?[] entries)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                if (entries[index] is not NeoScriptObject child)
                    continue;
                if (child.owner is not null || !TryAdoptDetachedChild(target, child))
                {
                    // A repeated entry would have two owners; undo this pass.
                    for (int undo = 0; undo < index; undo++)
                    {
                        if (entries[undo] is NeoScriptObject adopted)
                            adopted.owner = null;
                    }
                    return false;
                }
            }
            return true;
        }

        /// <summary>Drops the ownership a slot's previous value held.</summary>
        private static void ReleaseDetachedSlot(NeoScriptObject target, int index)
        {
            if (target.values[index] is NeoScriptObject previous)
            {
                previous.owner = null;
            }
            else if (target.values[index] is object?[] entries)
            {
                ReleaseDetachedEntries(entries);
            }
            else if (target.listBuffers?[index] is List<object?> buffer)
            {
                ReleaseDetachedEntries(buffer);
            }
            if (target.listBuffers is not null)
                target.listBuffers[index] = null;
        }

        private static void ReleaseDetachedEntries(IReadOnlyList<object?> entries)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index] is NeoScriptObject previousEntry)
                    previousEntry.owner = null;
            }
        }

        private static void ReleaseDetachedChildren(NeoScriptObject target)
        {
            for (int index = 0; index < target.values.Length; index++)
                ReleaseDetachedSlot(target, index);
        }

        /// <summary>
        /// Materializes a detached root and every detached object it owns as
        /// one staged Session graph: written slots are supplied values,
        /// untouched members get their ordinary class defaults, and the graph
        /// is prepared and published once.
        /// </summary>
        internal static string MaterializeDetached(
            NeoScriptObject root,
            NSGetterEvaluator.Context? scopeCtx)
        {
            NeoClient client = root.client;
            var scope = new NeoConstructionScope(client, scopeCtx);
            NeoTimestamp now = scopeCtx?.allocationTracker.ConstructionTimestamp
                ?? NeoTimestamp.Now();
            var rows = new List<MemberValue>();
            var staged = new List<(NeoScriptObject value, ObjectMemberValue row)>();
            Func<object?, NeoConstructorValueReference?> valueReference = null!;
            valueReference = value => value is NeoScriptObject child
                ? new NeoConstructorValueReference(
                    StageDetached(child, rows, now, scope, staged, valueReference).id,
                    null)
                : null;
            ObjectMemberValue rootRow = StageDetached(root, rows, now, scope, staged, valueReference);
            PrepareConstructedGraph(
                client,
                rootRow,
                rows,
                scope,
                // A constructor still running may not have set every member yet.
                requireCompleteRoot: !root.constructing,
                trustedMaterialization: true,
                root.plan.runtimePlan);
            client.PublishConstructedSessionRows(rows);
            foreach ((NeoScriptObject value, ObjectMemberValue row) in staged)
                value.attachedId = row.id;
            return rootRow.id;
        }

        /// <summary>Stamps a declared construction's recipe, serializing its deferred literal arguments.</summary>
        private static void StampDetachedProvenance(NeoScriptObject value, ObjectMemberValue row)
        {
            if (value.constructorArgs is null)
                return;
            if (value.constructorLiterals is not null)
            {
                foreach (KeyValuePair<string, object?[]> literal in value.constructorLiterals)
                    value.constructorArgs[literal.Key] = NeoClient.ConstructorArgumentToken(literal.Value, literal.Key);
                value.constructorLiterals = null;
            }
            NeoClient.StampConstructionProvenance(row, value.constructorId, value.constructorArgs);
        }

        private static ObjectMemberValue StageDetached(
            NeoScriptObject value,
            List<MemberValue> rows,
            NeoTimestamp now,
            NeoConstructionScope scope,
            List<(NeoScriptObject value, ObjectMemberValue row)> staged,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            DetachedClassPlan plan = value.plan;
            var supplied = new Dictionary<string, string>(plan.slots.Length);
            for (int index = 0; index < plan.slots.Length; index++)
            {
                if (value.states[index] != NeoScriptObject.WrittenSlot)
                    continue;
                DetachedSlot slot = plan.slots[index];
                string? childId = MaterializeRuntimeConstructorValue(
                    value.client,
                    slot.member,
                    slot.kind == DetachedSlotKind.List
                        ? DetachedListEntries(value, index)
                        : value.values[index],
                    rows,
                    now,
                    valueReference,
                    plan.runtimePlan.genericEnv,
                    $"{plan.classId}.{slot.schemaKey}",
                    scope.referenceOwnershipByPath,
                    preserveOptionalNull: true);
                if (childId is not null)
                    supplied[slot.schemaKey] = childId;
            }
            ObjectMemberValue row = CreateWritableClassValueRow(
                value.client,
                plan.classId,
                supplied,
                rows,
                now,
                scope,
                plan.classId,
                classPlan: plan.runtimePlan);
            StampDetachedProvenance(value, row);
            rows.Add(row);
            staged.Add((value, row));
            return row;
        }
    }
}
