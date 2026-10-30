// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        /// <summary>
        /// Save rows whose meaning depends on a slot the load couldn't place
        /// yet: a row under a world partition that hasn't loaded, or under a
        /// sparse instance before its replay. Each keeps the row as read and
        /// its chain of save parents, and is placed when a row on that chain
        /// gets a slot. A row the game rewrites meanwhile is
        /// dropped: every write is checked as it is made.
        /// </summary>
        private Dictionary<string, PendingSaveRow>? pendingSaveRows;

        private bool initializationCompleted;

        private sealed class PendingSaveRow
        {
            internal readonly MemberValue Row;

            /// <summary>The row's save ancestors, outermost first, ending with the row.</summary>
            internal readonly string[] Path;

            /// <summary>How many of them had a slot when it was left, so it is tried again only when another gets one.</summary>
            internal readonly int Slots;

            internal PendingSaveRow(MemberValue row, string[] path, int slots)
            {
                Row = row;
                Path = path;
                Slots = slots;
            }
        }

        /// <summary>A save value the pass refuses; each caller says what was not done.</summary>
        private sealed class SaveRowRefusal : Exception
        {
            internal SaveRowRefusal(string problem) : base(problem)
            {
            }
        }

        /// <summary>
        /// P127 §15 "Local saves". A save below revision 3 is refused, with
        /// no load-time upgrade. Rows are typed by shape, so a few values
        /// need their slot: a Float slot's symbol string is a float and an
        /// Int slot's <c>-0</c> is 0, and an out-of-range int or int vector
        /// component, or a null in a non-nullable slot, fails the load. Only
        /// those rows are placed: directly when their slot is known,
        /// otherwise through their save parents, and a row the load can't
        /// place yet is checked when its slot appears.
        /// </summary>
        private void ApplySaveFormatRevision(bool loadedExistingSave)
        {
            ProjectSaveData save = PersistedData;
            if (!loadedExistingSave)
            {
                save.requiredSaveFormatRevision = NeoSaveFormat.P127Revision;
                return;
            }
            NeoSaveFormat.RequireCurrent(save.requiredSaveFormatRevision, "The save was not loaded.");
            var pass = new SaveRowPass(this, save.values, plan: null);
            try
            {
                pass.AddStaticBindings(save.staticBindings);
                pass.AddStaticConstructions(save.staticConstructions);
                pass.AddRows(save.values);
                pass.Run();
            }
            catch (SaveRowRefusal refusal)
            {
                throw new NeoUnsupportedSaveFormatException(NeoSaveFormat.P127Revision, $"{refusal.Message} The save was not loaded.");
            }
            pendingSaveRows = pass.Pending;
        }

        /// <summary>
        /// Runs the save pass over external content before it is applied, so
        /// a live apply follows the same rules as a load and a refusal
        /// changes nothing. Its unplaced rows wait for
        /// <see cref="AdoptPendingSaveRows"/>.
        /// </summary>
        private Dictionary<string, PendingSaveRow> CheckExternalSaveRows(
            NeoGameSaveBase incoming,
            Dictionary<string, MemberValue> rows)
        {
            int? revision = incoming.requiredSaveFormatRevision;
            if ((revision ?? NeoSaveFormat.LegacyRevision) < NeoSaveFormat.P127Revision)
            {
                throw new NeoSaveValueRefusedException(
                    revision ?? NeoSaveFormat.LegacyRevision,
                    $"{NeoSaveFormat.BelowCurrent(revision)} The incoming save content was not applied.");
            }
            var pass = new SaveRowPass(this, rows, plan: null);
            try
            {
                pass.AddStaticBindings(incoming.staticBindings);
                pass.AddStaticConstructions(incoming.staticConstructions);
                pass.AddRows(rows);
                pass.Run();
            }
            catch (SaveRowRefusal refusal)
            {
                throw new NeoSaveValueRefusedException(NeoSaveFormat.P127Revision, $"{refusal.Message} The incoming save content was not applied.");
            }
            return pass.Pending;
        }

        /// <summary>
        /// After an external apply commits, its content is the whole save, so
        /// its unplaced rows replace the earlier ones. Each now names the row
        /// the store kept, which is the earlier object when nothing changed.
        /// </summary>
        private void AdoptPendingSaveRows(Dictionary<string, PendingSaveRow> pending)
        {
            Dictionary<string, PendingSaveRow>? adopted = null;
            foreach (KeyValuePair<string, PendingSaveRow> entry in pending)
            {
                if (!PersistedData.values.TryGetValue(entry.Key, out MemberValue? row))
                    continue;
                adopted ??= new Dictionary<string, PendingSaveRow>(StringComparer.Ordinal);
                adopted[entry.Key] = new PendingSaveRow(row, entry.Value.Path, entry.Value.Slots);
            }
            pendingSaveRows = adopted;
        }

        /// <summary>
        /// Places the waiting rows a sparse instance replay or a partition
        /// load just gave a slot: those with another row on their chain that
        /// now has one. Before initialization completes the next call covers
        /// them. A refusal throws <see cref="SaveRowRefusal"/> before
        /// anything changes.
        /// </summary>
        private void PlaceUnplacedSaveRows()
        {
            if (!initializationCompleted || pendingSaveRows is not { Count: > 0 } pending)
                return;
            SaveRowPass? pass = null;
            List<string>? stale = null;
            foreach (KeyValuePair<string, PendingSaveRow> entry in pending)
            {
                if (!PersistedData.values.TryGetValue(entry.Key, out MemberValue? row) || !ReferenceEquals(row, entry.Value.Row))
                    (stale ??= new List<string>()).Add(entry.Key);
                else if (CountStoredSlots(entry.Value.Path) > entry.Value.Slots)
                    (pass ??= new SaveRowPass(this, PersistedData.values, new NeoWritePlan(this))).AddPending(entry.Key, entry.Value);
            }
            if (pass is null && stale is null)
                return;
            pass?.Run();
            if (stale is not null)
            {
                foreach (string id in stale)
                    pending.Remove(id);
            }
            if (pass is not null)
            {
                foreach (string id in pass.Candidates)
                    pending.Remove(id);
                foreach (KeyValuePair<string, PendingSaveRow> entry in pass.Pending)
                    pending[entry.Key] = entry.Value;
                pass.Commit();
            }
        }

        private int CountStoredSlots(string[] valueIds)
        {
            int count = 0;
            foreach (string valueId in valueIds)
            {
                if (TryGetStoredSlot(valueId, out _))
                    count++;
            }
            return count;
        }

        /// <summary>The slot an authored or sparse-instance row fills, which a save row at its id overlays.</summary>
        private bool TryGetStoredSlot(string valueId, [NotNullWhen(true)] out Member? slot)
        {
            if (authoredIndexes.storedSlots.TryGetValue(valueId, out slot))
                return true;
            slot = virtualClassPlacementByChildId.TryGetValue(valueId, out VirtualClassPlacement? placement)
                ? placement.member
                : null;
            return slot is not null;
        }

        /// <summary>
        /// P127 §12.1. A Float slot's row holding a symbol string, as the
        /// number row it stores. Shape dispatch can't type it: <c>"-0"</c> is
        /// also a decimal, and every symbol is also a string.
        /// </summary>
        internal static bool TryDecodeFloatSymbolRow(MemberValue row, [NotNullWhen(true)] out NumberMemberValue? number)
        {
            number = null;
            if (row is not StringMemberValue { value: string text } || !NeoStoredFloat.IsSymbol(text))
                return false;
            number = new NumberMemberValue { value = NeoStoredFloat.DecodeSymbol(text) };
            CopyRowIdentity(row, number);
            return true;
        }

        /// <summary>
        /// The save pass. It collects the rows that need their slot and
        /// checks each one it can place: directly, through a static binding,
        /// or through its save parents, whose links it reads only when a row
        /// isn't placed directly. A row it can't place waits.
        /// </summary>
        private sealed class SaveRowPass
        {
            private readonly NeoClient client;
            private readonly Dictionary<string, MemberValue> store;
            private readonly NeoWritePlan? plan;
            private readonly HashSet<string> candidates = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Placement> seeds = new(StringComparer.Ordinal);
            private Dictionary<string, Placement?>? placements;
            private Dictionary<string, Dictionary<string, Member?>>? childMembers;
            private Dictionary<string, ParentLink>? parents;
            private Dictionary<string, List<string>>? otherParents;
            private bool scanned;
            private bool constructedSeeds;
            private Dictionary<string, GenericParamDeclaration>? classParams;
            private bool planned;

            internal SaveRowPass(NeoClient client, Dictionary<string, MemberValue> store, NeoWritePlan? plan)
            {
                this.client = client;
                this.store = store;
                this.plan = plan;
            }

            /// <summary>The rows this pass checks or leaves waiting.</summary>
            internal IEnumerable<string> Candidates => candidates;

            /// <summary>The rows it couldn't place.</summary>
            internal Dictionary<string, PendingSaveRow> Pending { get; } = new(StringComparer.Ordinal);

            internal void AddRows(Dictionary<string, MemberValue> rows)
            {
                foreach (MemberValue row in rows.Values)
                {
                    if (MayBeRefused(row) || NeedsSlotToRead(row))
                        candidates.Add(row.id);
                }
            }

            /// <summary>A waiting row, with the save parents it was found under.</summary>
            internal void AddPending(string id, PendingSaveRow pending)
            {
                candidates.Add(id);
                parents ??= new Dictionary<string, ParentLink>(StringComparer.Ordinal);
                for (int index = 1; index < pending.Path.Length; index++)
                    parents.TryAdd(pending.Path[index], new ParentLink(pending.Path[index - 1], null));
            }

            /// <summary>
            /// Static bindings are slots of their own, keyed by plain member
            /// id (P127 §5.3).
            /// </summary>
            internal void AddStaticBindings(Dictionary<string, string?> bindings)
            {
                foreach (KeyValuePair<string, string?> binding in bindings)
                {
                    if (binding.Key.Length == 0)
                        throw Refuse("A static binding key is empty.");
                    if (binding.Key.IndexOf('<') >= 0)
                        throw Refuse($"Static binding key '{binding.Key}' is not a member id; a generic static's construction binds in 'staticConstructions'.");
                    if (binding.Value is string valueId && client.TryGetMember(binding.Key, out Member? member))
                        seeds[valueId] = new Placement(member, null);
                }
            }

            /// <summary>
            /// Each construction of a generic static is a slot whose type
            /// parameters its closed head binds. A member and construction
            /// pair appears once.
            /// </summary>
            internal void AddStaticConstructions(List<NeoStaticConstruction>? constructions)
            {
                if (constructions is null)
                    return;
                var seen = new HashSet<(string, TypeBinding)>();
                for (int index = 0; index < constructions.Count; index++)
                {
                    NeoStaticConstruction entry = constructions[index];
                    string subject = $"Static construction {index}";
                    if (string.IsNullOrEmpty(entry.memberId))
                        throw Refuse($"{subject} has no memberId.");
                    subject = $"{subject} of member '{entry.memberId}'";
                    if (entry.construction is null)
                        throw Refuse($"{subject} has no construction.");
                    if (TypeBindings.PositionViolation(entry.construction, TypeBindingPosition.Identity) is string position)
                        throw Refuse($"{subject}: its construction {position}.");
                    TypeHead head = entry.construction.Head!;
                    if (head.Type != MemberKind.Class && head.Type != MemberKind.Interface)
                        throw Refuse($"{subject}: its construction is {NeoTypeNames.Of(entry.construction, client.ProjectDataForRuntime)}, but a construction names a Class or Interface.");
                    if (client.TypeRules.KeyViolation(entry.construction) is string violation)
                        throw Refuse($"{subject}: its construction {violation}.");
                    if (!seen.Add((entry.memberId, entry.construction)))
                        throw Refuse($"Save holds two entries for static '{GenericStaticIndex.Of(client.ProjectDataForRuntime)?.Label(entry.memberId) ?? entry.memberId}' of construction '{NeoTypeNames.Of(entry.construction, client.ProjectDataForRuntime)}'.");
                    if (!client.TryGetMember(entry.memberId, out Member? member))
                        throw Refuse($"{subject}: its member no longer exists.");
                    if (head.Type == MemberKind.Interface && (!client.ProjectDataForRuntime.interfaces.TryGetValue(head.InterfaceId!, out var interfaceOwner)
                        || !interfaceOwner.members.Values.Any(declaration => declaration.IsStatic && declaration.implementationMemberId == entry.memberId)))
                        throw Refuse($"{subject}: its construction does not name the member's interface.");
                    if (client.FindSchemaPlacement(member.id)?.ownerClass is { } owner
                        && (head.Type != MemberKind.Class || head.ClassId != owner.id))
                    {
                        throw Refuse($"{subject}: its construction {NeoTypeNames.Of(entry.construction, client.ProjectDataForRuntime)} doesn't name the member's class {owner.name}.");
                    }
                    if (member.Modifier != NeoMemberModifierKind.Static || entry.construction.Bindings is not { Count: > 0 })
                        throw Refuse($"{subject}: the member must be static and its owner generic.");
                    if (entry.value is not string valueId)
                        continue;
                    Dictionary<string, Member>? construction = Construction(entry.construction);
                    constructedSeeds |= construction is not null;
                    seeds[valueId] = new Placement(member, construction);
                }
            }

            /// <summary>Whether a row's slot may refuse it: a number outside int32 or a null.</summary>
            private static bool MayBeRefused(MemberValue row) => HasOutOfRangeNumber(row) || IsNullRow(row);

            /// <summary>Whether a row, or a constructor argument of it, holds a number or vector component outside int32.</summary>
            private static bool HasOutOfRangeNumber(MemberValue row) => row switch
            {
                NumberMemberValue { value: double number } => OutsideInt32(number),
                Vector2MemberValue { value: { } vector } => OutsideInt32(vector.x) || OutsideInt32(vector.y),
                Vector3MemberValue { value: { } vector } => OutsideInt32(vector.x)
                    || OutsideInt32(vector.y)
                    || OutsideInt32(vector.z),
                ObjectMemberValue { constructorArgs: { Count: > 0 } arguments } => HasOutOfRangeNumber(arguments),
                _ => false,
            };

            /// <summary>
            /// Whether a row reads differently by slot on every load: a
            /// symbol string is a float only in a Float slot, and a
            /// <c>-0</c> is 0 in an int slot.
            /// </summary>
            private static bool NeedsSlotToRead(MemberValue row) => row switch
            {
                StringMemberValue { value: string text } => NeoStoredFloat.IsSymbol(text),
                NumberMemberValue { value: double number } => IsNegativeZero(number),
                Vector3MemberValue { value: { } vector } => IsNegativeZero(vector.x) || IsNegativeZero(vector.y) || IsNegativeZero(vector.z),
                Vector2MemberValue { value: { } vector } => IsNegativeZero(vector.x) || IsNegativeZero(vector.y),
                _ => false,
            };

            /// <summary>Checks every candidate it can place and leaves the rest in <see cref="Pending"/>.</summary>
            internal void Run()
            {
                List<string>? unplaced = null;
                foreach (string candidate in candidates)
                {
                    if (Direct(candidate) is Placement placement)
                        Check(candidate, placement);
                    else
                        (unplaced ??= new List<string>()).Add(candidate);
                }
                if (unplaced is null)
                    return;
                // A waiting row arrives with its parents; any other row needs
                // the save's links, and so does a waiting row whose recorded
                // parents no longer place it.
                if (parents is null)
                    ScanParents();
                List<string>? retry = null;
                foreach (string id in unplaced)
                {
                    if (Place(id) is Placement placement)
                        Check(id, placement);
                    else if (!scanned && client.CountStoredSlots(PathTo(id)) > 0)
                        (retry ??= new List<string>()).Add(id);
                    else
                        Leave(id);
                }
                if (retry is null)
                    return;
                ScanParents();
                foreach (string id in retry)
                {
                    if (Place(id) is Placement placement)
                        Check(id, placement);
                    else
                        Leave(id);
                }
            }

            private void Leave(string id)
            {
                string[] path = PathTo(id);
                Pending[id] = new PendingSaveRow(store[id], path, client.CountStoredSlots(path));
            }

            internal void Commit()
            {
                if (planned)
                    plan!.Commit();
            }

            private sealed class Placement
            {
                internal readonly Member Member;
                internal readonly Dictionary<string, Member>? Construction;

                internal Placement(Member member, Dictionary<string, Member>? construction)
                {
                    Member = member;
                    Construction = construction;
                }

                /// <summary>The slot, its type parameters bound by the construction.</summary>
                internal Member Slot => Bind(Member, Construction);
            }

            /// <summary>A row's save parent, and the field it fills there.</summary>
            private readonly struct ParentLink
            {
                internal readonly string ParentId;
                internal readonly string? Field;

                internal ParentLink(string parentId, string? field)
                {
                    ParentId = parentId;
                    Field = field;
                }
            }

            /// <summary>A row with a slot of its own: a static binding's, an authored row's or a sparse instance's.</summary>
            private Placement? Direct(string id)
            {
                if (seeds.TryGetValue(id, out Placement? seed))
                    return seed;
                return client.TryGetStoredSlot(id, out Member? slot) ? new Placement(slot, null) : null;
            }

            /// <summary>
            /// A row's slot: its own, else its field's declaration when that
            /// needs no context, else its parent's child slot. Memoized; a
            /// cycle places nothing.
            /// </summary>
            private Placement? Place(string id)
            {
                placements ??= new Dictionary<string, Placement?>(StringComparer.Ordinal);
                if (placements.TryGetValue(id, out Placement? known))
                    return known;
                placements[id] = null;
                Placement? placed = Direct(id);
                if (placed is null && parents!.TryGetValue(id, out ParentLink link))
                {
                    placed = FieldDeclaration(link) ?? ChildOf(link.ParentId, id);
                    if (placed is null && otherParents is not null && otherParents.TryGetValue(id, out List<string>? others))
                    {
                        foreach (string parentId in others)
                        {
                            if ((placed = ChildOf(parentId, id)) is not null)
                                break;
                        }
                    }
                }
                placements[id] = placed;
                return placed;
            }

            /// <summary>
            /// A class instance's field whose declaration closes over nothing
            /// is placed without its parent's slot. A constructed static
            /// binding carries type arguments down, so with one it climbs, as
            /// does a field whose arguments name its owner's params.
            /// </summary>
            private Placement? FieldDeclaration(ParentLink link)
            {
                if (link.Field is not string field
                    || constructedSeeds
                    || Row(link.ParentId) is not ObjectMemberValue { classId: string classId }
                    || !client.TryResolveMergedSchemaMember(classId, field, out Member? declared)
                    || NeedsOwnedMemberContext(declared)
                    || NeoGenericResolution.ReadsOwnerParams(client, declared))
                {
                    return null;
                }
                return new Placement(declared, null);
            }

            /// <summary>The slot <paramref name="parentId"/>'s row gives <paramref name="childId"/>, once its own is known.</summary>
            private Placement? ChildOf(string parentId, string childId)
            {
                if (Place(parentId) is not Placement parent || Row(parentId) is not MemberValue parentRow)
                    return null;
                Member parentSlot = parent.Slot;
                childMembers ??= new Dictionary<string, Dictionary<string, Member?>>(StringComparer.Ordinal);
                if (!childMembers.TryGetValue(parentId, out Dictionary<string, Member?>? children))
                {
                    childMembers[parentId] = children = new Dictionary<string, Member?>(StringComparer.Ordinal);
                    foreach (var child in client.EnumerateOwnedChildLinks(parentRow, parentSlot))
                        children.TryAdd(child.valueId, child.member);
                }
                if (children.TryGetValue(childId, out Member? member))
                    return member is null ? null : new Placement(member, parent.Construction);
                // An unordered collection's entries name their container.
                if (Row(childId)?.containerId == parentId
                    && client.TryResolveCollectionEntryMember(parentSlot, parentRow) is Member entry)
                {
                    return new Placement(entry, parent.Construction);
                }
                return null;
            }

            private MemberValue? Row(string id) => store.TryGetValue(id, out MemberValue? row) ? row : client.ResolveValueRow(id);

            /// <summary>
            /// One read of the save's links: object fields, settled aggregate
            /// arguments and containment stamps, then array entries. A row
            /// with several parents keeps the others to try.
            /// </summary>
            private void ScanParents()
            {
                scanned = true;
                placements = null;
                childMembers = null;
                otherParents = null;
                parents = new Dictionary<string, ParentLink>(StringComparer.Ordinal);
                foreach (MemberValue row in store.Values)
                {
                    if (row is ObjectMemberValue objectRow)
                    {
                        if (objectRow.value is not null)
                        {
                            foreach (KeyValuePair<string, string> field in objectRow.value)
                            {
                                if (field.Value is not null)
                                    AddParent(field.Value, new ParentLink(row.id, field.Key));
                            }
                        }
                        if (objectRow.constructorArgs is not null)
                        {
                            foreach (var link in client.EnumerateConstructorSettledAggregateLinks(objectRow))
                                AddParent(link.valueId, new ParentLink(row.id, null));
                        }
                    }
                    if (row.containerId is string containerId)
                        AddParent(row.id, new ParentLink(containerId, null));
                }
                foreach (MemberValue row in store.Values)
                {
                    if (row is not ArrayMemberValue { value: { } entries })
                        continue;
                    foreach (string entryId in entries)
                    {
                        if (entryId is not null)
                            AddParent(entryId, new ParentLink(row.id, null));
                    }
                }
            }

            private void AddParent(string childId, ParentLink link)
            {
                if (parents!.TryAdd(childId, link) || parents[childId].ParentId == link.ParentId)
                    return;
                otherParents ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
                if (!otherParents.TryGetValue(childId, out List<string>? others))
                    otherParents[childId] = others = new List<string>();
                others.Add(link.ParentId);
            }

            /// <summary>A row's save ancestors by first parent, outermost first, ending with the row.</summary>
            private string[] PathTo(string id)
            {
                var path = new List<string> { id };
                var seen = new HashSet<string>(StringComparer.Ordinal) { id };
                string cursor = id;
                while (parents!.TryGetValue(cursor, out ParentLink link) && seen.Add(link.ParentId))
                {
                    path.Add(link.ParentId);
                    cursor = link.ParentId;
                }
                path.Reverse();
                return path.ToArray();
            }

            /// <summary>Checks a placed row, replacing it when it reads differently in its slot.</summary>
            private void Check(string id, Placement placement)
            {
                MemberValue row = store[id];
                if (row.IsRemoved)
                    return;
                Member member = placement.Slot;
                if (member is FloatMember && TryDecodeFloatSymbolRow(row, out NumberMemberValue? decoded))
                {
                    Replace(decoded);
                    return;
                }
                if (IntZero(row, member) is MemberValue normalized)
                    row = Replace(normalized);
                if (IntComponents(member, row) is double[] components)
                {
                    if (IntRefusal($"Save value '{row.id}' ({member.name})", components) is string refusal)
                        throw Refuse(refusal);
                    return;
                }
                if (row is ObjectMemberValue obj)
                {
                    CheckConstructorArguments(obj, member);
                    return;
                }
                if (IsNullRow(row) && IsNonNullable(member))
                    throw Refuse($"Save value '{row.id}' ({member.name}) holds null in a non-nullable slot.");
            }

            /// <summary>
            /// An int slot's row holding <c>-0</c>, as 0: an int has no
            /// negative zero (P127 §12.1), and only a float position writes
            /// the <c>"-0"</c> symbol.
            /// </summary>
            private MemberValue? IntZero(MemberValue row, Member member)
            {
                switch (member, row)
                {
                    case (IntMember, NumberMemberValue { value: double number }) when IsNegativeZero(number):
                        {
                            var copy = (NumberMemberValue)client.CloneRowForWrite(row);
                            copy.value = 0d;
                            return copy;
                        }
                    case (IntMember, StringMemberValue { value: "-0" }):
                        {
                            var zero = new NumberMemberValue { value = 0d };
                            CopyRowIdentity(row, zero);
                            return zero;
                        }
                    case (Vector3IntMember, Vector3MemberValue { value: { } vector })
                        when IsNegativeZero(vector.x) || IsNegativeZero(vector.y) || IsNegativeZero(vector.z):
                        {
                            var copy = (Vector3MemberValue)client.CloneRowForWrite(row);
                            copy.value = new NeoVector3Value { x = vector.x + 0d, y = vector.y + 0d, z = vector.z + 0d };
                            return copy;
                        }
                    case (Vector2IntMember, Vector2MemberValue { value: { } vector })
                        when IsNegativeZero(vector.x) || IsNegativeZero(vector.y):
                        {
                            var copy = (Vector2MemberValue)client.CloneRowForWrite(row);
                            copy.value = new NeoVector2Value { x = vector.x + 0d, y = vector.y + 0d };
                            return copy;
                        }
                    default:
                        return null;
                }
            }

            private void CheckConstructorArguments(ObjectMemberValue row, Member slot)
            {
                if (row.constructorArgs is not { Count: > 0 } arguments
                    || row.instanceConstructorId is not string constructorId
                    || !client.data.constructors.TryGetValue(constructorId, out ConstructorRecord? constructor))
                {
                    return;
                }
                IReadOnlyDictionary<string, NeoGenericEnvEntry>? environment = null;
                for (int index = 0; index < constructor.argumentTypes.Length; index++)
                {
                    FunctionArgumentTypeInfo argument = constructor.argumentTypes[index];
                    string parameterId = ConstructorParameterId(constructor, index);
                    if (!arguments.TryGetValue(parameterId, out JToken? token)
                        || token is null
                        || !HasOutOfRangeNumber(token))
                    {
                        continue;
                    }
                    MemberKind kind = argument.type;
                    if (argument.type == MemberKind.Generic && argument.genericParamId is string paramId)
                    {
                        environment ??= NeoGenericResolution.SlotRowEnv(
                            client,
                            slot,
                            slot.ownerEnvironment,
                            row.genericBindings,
                            row.classId ?? constructor.classId);
                        if (!environment.TryGetValue(paramId, out NeoGenericEnvEntry? binding)
                            || binding.memberId is not string bindingId
                            || !client.TryGetMember(bindingId, out Member? bound))
                        {
                            continue;
                        }
                        kind = bound.kind;
                    }
                    if (IntComponents(kind, token) is double[] components
                        && IntRefusal($"Save value '{row.id}' constructor argument '{argument.name}'", components) is string refusal)
                    {
                        throw Refuse(refusal);
                    }
                }
            }

            private MemberValue Replace(MemberValue row)
            {
                if (plan is null)
                {
                    store[row.id] = row;
                }
                else
                {
                    plan.Set(client.PersistedOwnership, row, silent: true);
                    planned = true;
                }
                return row;
            }

            /// <summary>
            /// Binds each type parameter of a construction's head whose
            /// argument makes a slot (§1.3). A reference type's argument
            /// can't say whether it is nullable, so its slot is optional.
            /// </summary>
            private Dictionary<string, Member>? Construction(TypeBinding head)
            {
                if (head.Bindings is null)
                    return null;
                Dictionary<string, Member>? construction = null;
                foreach (KeyValuePair<string, TypeBinding> argument in head.Bindings)
                {
                    if (NeoTypeBindingViews.PayloadView(argument.Value, out _) is Member slot)
                        (construction ??= new Dictionary<string, Member>(StringComparer.Ordinal))[argument.Key] = slot;
                }
                return construction;
            }

            /// <summary>The type argument a construction slot stands for, its optional reference type kept optional.</summary>
            private static TypeBinding SlotBinding(Member slot)
            {
                TypeBinding binding = NeoTypeBindingViews.BindingOf(slot.id)!;
                return slot.Requirement == NeoMemberRequirementKind.Required ? binding : binding.WithRequired(false);
            }

            /// <summary>A construction's slot for a member on its type parameter.</summary>
            private static Member Bind(Member member, Dictionary<string, Member>? construction)
            {
                if (construction is null)
                    return member;
                if (member is GenericMember generic)
                {
                    if (!construction.TryGetValue(generic.genericParamId, out Member? bound))
                        return member;
                    Member slot = bound.ShallowClone();
                    slot.id = generic.id;
                    slot.name = generic.name;
                    return slot;
                }
                // A constructed slot forwarding a parameter closes over its argument too.
                if (member is not ClassMember constructed
                    || constructed.TypeArguments is not { } arguments
                    || !arguments.Values.Any(argument => argument.IsOpen && construction.ContainsKey(argument.Param!)))
                {
                    return member;
                }
                var closed = new Dictionary<string, TypeBinding>(arguments.Count, StringComparer.Ordinal);
                foreach (KeyValuePair<string, TypeBinding> pair in arguments)
                {
                    closed[pair.Key] = pair.Value.IsOpen && construction.TryGetValue(pair.Value.Param!, out Member? argument)
                        ? SlotBinding(argument)
                        : pair.Value;
                }
                var copy = (ClassMember)constructed.ShallowClone();
                if (constructed.DeclaredInterfaceId is null)
                    copy.classArguments = closed;
                else
                    copy.interfaceArguments = closed;
                return copy;
            }

            /// <summary>
            /// Whether a slot refuses null: a required one, except a
            /// <c>T?</c> declaration, which refuses it only where P127 makes
            /// it the value binding's type (§4.2). An unbound type parameter
            /// is left alone.
            /// </summary>
            private bool IsNonNullable(Member member)
            {
                if (member is GenericMember)
                    return false;
                if (client.TryGetMember(member.id, out Member? declared)
                    && declared is GenericMember { Requirement: NeoMemberRequirementKind.Optional })
                {
                    return IsNullableTypeParameterSlot(member);
                }
                return member.Requirement == NeoMemberRequirementKind.Required;
            }

            /// <summary>
            /// P127 §4.2. A slot declared <c>T?</c> on a type parameter that
            /// is unconstrained, or constrained only by interfaces or by
            /// <c>Enum</c>, bound to a value kind.
            /// </summary>
            private bool IsNullableTypeParameterSlot(Member member)
            {
                if (!IsValueKind(member)
                    || !client.TryGetMember(member.id, out Member? declared)
                    || declared is not GenericMember generic
                    || generic.Requirement != NeoMemberRequirementKind.Optional)
                {
                    return false;
                }
                if (classParams is null)
                {
                    classParams = new Dictionary<string, GenericParamDeclaration>(StringComparer.Ordinal);
                    foreach (NeoSchemaClass schemaClass in client.data.classes.Values)
                    {
                        if (schemaClass.genericParams is null)
                            continue;
                        foreach (GenericParamDeclaration param in schemaClass.genericParams)
                            classParams[param.id] = param;
                    }
                }
                if (!classParams.TryGetValue(generic.genericParamId, out GenericParamDeclaration? declaration))
                    return false;
                if (declaration.constraints is null)
                    return true;
                foreach (GenericParamConstraint constraint in declaration.constraints)
                {
                    if (constraint.kind != NeoGenericParamConstraintKind.Interface
                        && constraint.kind != NeoGenericParamConstraintKind.AnyEnum)
                    {
                        return false;
                    }
                }
                return true;
            }

            private static bool IsValueKind(Member member) => member is IntMember or FloatMember or DecimalMember
                or BoolMember or EnumMember or TupleMember or Vector2Member or Vector2IntMember
                or Vector3Member or Vector3IntMember or ColorMember;

            /// <summary>A null value; a row with a class id is an instance, whatever its value.</summary>
            private static bool IsNullRow(MemberValue row) => row.classId is null && row switch
            {
                NullMemberValue value => value.value is null,
                NumberMemberValue value => value.value is null,
                BoolMemberValue value => value.value is null,
                StringMemberValue value => value.value is null,
                ArrayMemberValue value => value.value is null,
                Vector2MemberValue value => value.value is null,
                Vector3MemberValue value => value.value is null,
                ColorMemberValue value => value.value is null,
                _ => false,
            };

            private static bool HasOutOfRangeNumber(Dictionary<string, JToken?> arguments)
            {
                foreach (JToken? token in arguments.Values)
                {
                    if (token is not null && HasOutOfRangeNumber(token))
                        return true;
                }
                return false;
            }

            /// <summary>Whether a constructor argument holds a number, or an {x, y(, z)} component, outside int32.</summary>
            private static bool HasOutOfRangeNumber(JToken token)
            {
                if (token is not JObject vector)
                    return IsNumber(token) && OutsideInt32(token.Value<double>());
                foreach (string axis in Axes)
                {
                    if (vector[axis] is JToken component && IsNumber(component) && OutsideInt32(component.Value<double>()))
                        return true;
                }
                return false;
            }

            private static bool IsNumber(JToken token) => token.Type is JTokenType.Integer or JTokenType.Float;

            /// <summary>An int slot's row as its one component, an int vector slot's as its axes; null for any other slot or row.</summary>
            private static double[]? IntComponents(Member slot, MemberValue row) => (slot, row) switch
            {
                (IntMember, NumberMemberValue) or (Vector2IntMember, Vector2MemberValue) or (Vector3IntMember, Vector3MemberValue) => RowComponents(row),
                _ => null,
            };

            private static double[]? RowComponents(MemberValue row) => row switch
            {
                NumberMemberValue { value: double number } => new[] { number },
                Vector2MemberValue { value: { } vector } => new[] { vector.x, vector.y },
                Vector3MemberValue { value: { } vector } => new[] { vector.x, vector.y, vector.z },
                _ => null,
            };

            /// <summary>A constructor argument of an int or int vector type, as its components; null for another type or shape.</summary>
            private static double[]? IntComponents(MemberKind kind, JToken token)
            {
                int count = kind switch
                {
                    MemberKind.Int => 1,
                    MemberKind.Vector2Int => 2,
                    MemberKind.Vector3Int => 3,
                    _ => 0,
                };
                if (count == 1)
                    return IsNumber(token) ? new[] { token.Value<double>() } : null;
                if (count == 0 || token is not JObject vector || vector.Count != count)
                    return null;
                var components = new double[count];
                for (int index = 0; index < count; index++)
                {
                    if (!NeoStoredFloat.TryDecode(vector[Axes[index]], out components[index]))
                        return null;
                }
                return components;
            }

            /// <summary>
            /// Revision 3's refusal of the first component outside int32; null
            /// when every one fits.
            /// </summary>
            private static string? IntRefusal(string subject, double[] components)
            {
                for (int index = 0; index < components.Length; index++)
                {
                    string holder = components.Length == 1 ? subject : $"{subject} field '{Axes[index]}'";
                    double component = components[index];
                    if (OutsideInt32(component))
                        return $"{holder} holds {Spell(component)}, which is outside the int range.";
                }
                return null;
            }

            private static readonly string[] Axes = { "x", "y", "z" };

            private static bool OutsideInt32(double number) => number < int.MinValue || number > int.MaxValue;

            private static bool IsNegativeZero(double number) => number == 0 && double.IsNegative(number);

            private static string Spell(double number) => number.ToString("R", CultureInfo.InvariantCulture);

            private static SaveRowRefusal Refuse(string problem) => new(problem);
        }
    }
}
