// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>How a box's payload stores (P127 §1.3).</summary>
    internal enum NeoBoxFormKind
    {
        /// <summary>The payload stores as its payload view (<see cref="NeoTypeBindingViews.PayloadView"/>) stores its value.</summary>
        Member = 0,
        /// <summary>A held built-in comparer: a <c>BuiltInComparer</c> payload (§12.1).</summary>
        Comparer = 1,
        Invalid = 2,
        /// <summary><c>new object()</c>: identity only, with the empty payload <c>{}</c> (§1.1).</summary>
        Identity = 3,
    }

    internal readonly struct NeoBoxForm
    {
        internal readonly NeoBoxFormKind Kind;
        /// <summary>The payload view of a <see cref="NeoBoxFormKind.Member"/> box.</summary>
        internal readonly Member? Member;
        /// <summary>Why an <see cref="NeoBoxFormKind.Invalid"/> key holds no value.</summary>
        internal readonly string? Invalid;

        internal NeoBoxForm(NeoBoxFormKind kind, Member? member = null, string? invalid = null)
        {
            Kind = kind;
            Member = member;
            Invalid = invalid;
        }
    }

    /// <summary>
    /// The generic scope a stored row's slot resolves in: the parent row
    /// and slot whose instance or collection environment closes a type
    /// parameter the slot forwards, and a save construction's arguments.
    /// The server's <c>options.genericEnv</c>.
    /// </summary>
    internal readonly struct NeoSlotContext
    {
        internal readonly NeoClient? Client;
        internal readonly MemberValue? ParentRow;
        internal readonly Member? ParentMember;
        internal readonly Dictionary<string, Member>? Construction;

        internal NeoSlotContext(
            NeoClient client,
            MemberValue? parentRow = null,
            Member? parentMember = null,
            Dictionary<string, Member>? construction = null)
        {
            Client = client;
            ParentRow = parentRow;
            ParentMember = parentMember;
            Construction = construction;
        }

        /// <summary>The parent's environment, with each construction argument closing a parameter it leaves open.</summary>
        internal IReadOnlyDictionary<string, NeoGenericEnvEntry> Environment()
        {
            IReadOnlyDictionary<string, NeoGenericEnvEntry> environment = Client is null
                ? NeoGenericResolution.EmptyEnv
                : Client.ChildGenericEnvironment(ParentRow, ParentMember);
            if (Construction is not { Count: > 0 })
                return environment;
            var merged = new Dictionary<string, NeoGenericEnvEntry>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, NeoGenericEnvEntry> entry in environment)
                merged[entry.Key] = entry.Value;
            foreach (KeyValuePair<string, Member> argument in Construction)
            {
                if (!merged.TryGetValue(argument.Key, out NeoGenericEnvEntry? existing) || !existing.IsBound)
                    merged[argument.Key] = NeoGenericEnvEntry.Bound(argument.Value.id);
            }
            return merged;
        }
    }

    /// <summary>
    /// P127 §1.3, §5.3 and §9.5. The stored-type rules of one export: how a
    /// box's payload stores, a closed binding's in-memory runtime type key,
    /// which slots admit which stored rows, and conformance between runtime
    /// types. Cached per export. A payload's member is its binding's view
    /// (<see cref="NeoTypeBindingViews.PayloadView"/>).
    /// </summary>
    internal sealed class NeoTypeRules
    {

        private const string ObjectKey = "object";
        private const string NumberStyles = "NumberStyles";
        private const string NeoDelegate = "NeoDelegate";
        private const string NeoAction = "NeoAction";

        private readonly ProjectData? data;
        private readonly Dictionary<TypeBinding, NeoBoxForm> boxes = new Dictionary<TypeBinding, NeoBoxForm>();
        private readonly Dictionary<TypeBinding, string?> keyTexts = new Dictionary<TypeBinding, string?>();
        private readonly Dictionary<TypeBinding, string> canonicalKeys = new Dictionary<TypeBinding, string>();
        private readonly Dictionary<(string Row, string Base), Dictionary<string, TypeInfo>?> openRowBaseArguments =
            new Dictionary<(string Row, string Base), Dictionary<string, TypeInfo>?>();

        internal NeoTypeRules(ProjectData? data) => this.data = data;

        /// <summary>The scope of one export, shared by every client of it.</summary>
        internal static NeoTypeRules Of(ProjectData data) => data.typeRules ??= new NeoTypeRules(data);

        /// <summary>
        /// How a box's payload stores (§1.3): as a field of its held type's
        /// member, except that a class names the public class of a held
        /// built-in comparer and <c>object</c> is <c>new object()</c>. Cached
        /// per binding.
        /// </summary>
        internal NeoBoxForm Box(TypeBinding valueType)
        {
            if (boxes.TryGetValue(valueType, out NeoBoxForm cached))
                return cached;
            NeoBoxForm form = KeyOf(valueType) is string key
                ? UncachedBox(key, valueType)
                : new NeoBoxForm(NeoBoxFormKind.Invalid, invalid: $"valueType {KeyViolation(valueType)}");
            boxes[valueType] = form;
            return form;
        }

        /// <summary>
        /// The in-memory runtime type key (§5.3) of a closed stored binding,
        /// with class and interface arguments in declaration order; null when
        /// the binding names no runtime type this export declares. Cached
        /// per binding, and never stored.
        /// </summary>
        internal string? KeyOf(TypeBinding binding)
        {
            if (keyTexts.TryGetValue(binding, out string? cached))
                return cached;
            string? key = KeyViolation(binding) is null ? SpellKey(binding) : null;
            keyTexts[binding] = key;
            return key;
        }

        /// <summary>The server's sorted-JSON binding key, cached for stored Set entry keys.</summary>
        internal string CanonicalKeyOf(TypeBinding binding)
        {
            if (!canonicalKeys.TryGetValue(binding, out string key))
            {
                key = NeoTypeSources.Key(binding);
                canonicalKeys[binding] = key;
            }
            return key;
        }

        /// <summary>Why <paramref name="binding"/> has no runtime type key, or null.</summary>
        internal string? KeyViolation(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return $"is a {binding.Kind} binding; only a closed type has a runtime type";
            TypeHead head = binding.Head!;
            if (head.IsOpaque)
                return $"names opaque type '{head.OpaqueName}'; only a Type payload names one";
            switch (head.Type)
            {
                case MemberKind.Class:
                case MemberKind.Interface:
                    {
                        bool isClass = head.Type == MemberKind.Class;
                        string id = (isClass ? head.ClassId : head.InterfaceId)!;
                        string kind = isClass ? "class" : "interface";
                        bool found = isClass
                            ? data?.classes?.ContainsKey(id) == true
                            : data?.interfaces?.ContainsKey(id) == true;
                        if (!found)
                            return $"names missing {kind} '{id}'";
                        List<GenericParamDeclaration> parameters = ParametersOf(isClass ? NeoRuntimeTypeKeyKind.Class : NeoRuntimeTypeKeyKind.Interface, id);
                        string name = isClass ? ClassName(id) : InterfaceName(id);
                        foreach (GenericParamDeclaration parameter in parameters)
                        {
                            if (binding.Bindings is null || !binding.Bindings.ContainsKey(parameter.id))
                                return $"doesn't bind {name}'s type parameter {parameter.name}";
                        }
                        int bound = binding.Bindings?.Count ?? 0;
                        if (bound != parameters.Count)
                            return $"binds {bound} type arguments, but {name} takes {parameters.Count}";
                        break;
                    }
                case MemberKind.Object when ObjectTypeName(head.ObjectType) is null:
                    return $"names {head.ObjectType}, which has no source type";
            }
            if (binding.Bindings is null)
                return null;
            foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
            {
                if (KeyViolation(pair.Value) is string nested)
                    return $"at \"{pair.Key}\" {nested}";
            }
            return null;
        }

        /// <summary>The key text of a binding <see cref="KeyViolation"/> accepts.</summary>
        private string SpellKey(TypeBinding binding)
        {
            TypeHead head = binding.Head!;
            string suffix = head.Required ? "" : "?";
            switch (head.Type)
            {
                case MemberKind.Class:
                    return Constructed("c:" + head.ClassId, NeoRuntimeTypeKeyKind.Class, head.ClassId!, binding) + suffix;
                case MemberKind.Interface:
                    return Constructed("i:" + head.InterfaceId, NeoRuntimeTypeKeyKind.Interface, head.InterfaceId!, binding) + suffix;
                case MemberKind.Enum:
                    return "e:" + head.EnumId + suffix;
                case MemberKind.Tuple:
                    return Positional("(", binding, ")") + suffix;
                case MemberKind.List:
                    return Positional("List<", binding, ">") + suffix;
                case MemberKind.Set:
                    return Positional("Set<", binding, ">") + suffix;
                case MemberKind.Reference:
                    return Positional("Reference<", binding, ">") + suffix;
                case MemberKind.Dictionary:
                    return Positional("Dictionary<", binding, ">") + suffix;
                case MemberKind.NSDelegate:
                    return Positional(NeoDelegate + "<", binding, ">") + suffix;
                case MemberKind.NSAction:
                    return (binding.Bindings is null ? NeoAction : Positional(NeoAction + "<", binding, ">")) + suffix;
                case MemberKind.Object:
                    return ObjectTypeName(head.ObjectType) + suffix;
                case MemberKind.Void:
                    return "void";
                case MemberKind.String:
                    return "string" + suffix;
                case MemberKind.Sprite:
                    return "SpriteInfo" + suffix;
                case MemberKind.Audio:
                    return "AudioClipInfo" + suffix;
                case MemberKind.DialogueLookup:
                    return "NeoDialogueReference" + suffix;
                default:
                    return BuiltinName(head.Type) + suffix;
            }
        }

        private static string BuiltinName(MemberKind kind) => kind switch
        {
            MemberKind.Int => "int",
            MemberKind.Float => "float",
            MemberKind.Decimal => "decimal",
            MemberKind.Bool => "bool",
            _ => kind.ToString(),
        };

        /// <summary>A built-in head's positions <c>"0"</c> to <c>"n-1"</c>, spelled in order.</summary>
        private string Positional(string open, TypeBinding binding, string close)
        {
            int count = binding.Bindings?.Count ?? 0;
            var parts = new string[count];
            for (int index = 0; index < count; index++)
                parts[index] = SpellKey(binding.Bindings![index.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            return open + string.Join(",", parts) + close;
        }

        /// <summary>A declared head's arguments in its parameters' declaration order.</summary>
        private string Constructed(string spelled, NeoRuntimeTypeKeyKind kind, string id, TypeBinding binding)
        {
            List<GenericParamDeclaration> parameters = ParametersOf(kind, id);
            if (parameters.Count == 0)
                return spelled;
            var parts = new string[parameters.Count];
            for (int index = 0; index < parts.Length; index++)
                parts[index] = SpellKey(binding.Bindings![parameters[index].id]);
            return spelled + "<" + string.Join(",", parts) + ">";
        }

        /// <summary>
        /// The member whose shape a stored row's payload takes (§1.3): a box's
        /// payload view, else the slot. Null for a held comparer, a
        /// <c>new object()</c> box or an unreadable key, whose payload owns
        /// no rows. A class row in an <c>object</c> slot keeps the slot: its
        /// fields resolve through the row's <c>classId</c>.
        /// </summary>
        internal Member? PayloadMemberOf(Member? slot, MemberValue row)
        {
            if (row.valueType is not TypeBinding valueType)
                return slot;
            NeoBoxForm form = Box(valueType);
            return form.Kind == NeoBoxFormKind.Member ? form.Member : null;
        }

        /// <summary>
        /// Why <paramref name="row"/> can't be stored under
        /// <paramref name="slot"/>, or null (§1.3, §9.5), starting
        /// <c>value "{id}"</c>: a box needs a
        /// readable key its slot admits, <c>new object()</c> only an
        /// <c>object</c> slot, an authored comparer's row is a class row, and
        /// only a Set or Dictionary carries a comparer. Worded as the server's
        /// <c>stored-value-graph-semantics.ts</c>. <paramref name="context"/>
        /// is the generic scope a box's interface slot resolves in.
        /// </summary>
        internal string? SlotViolation(Member slot, MemberValue row, in NeoSlotContext context)
        {
            // Most slots hold no box, no comparer and no object; an unbound
            // type parameter is judged where it is closed.
            if (row.valueType is null && row.comparer is null && slot is not ObjectMember)
                return null;
            if (slot is GenericMember)
                return null;
            if (slot.id == NeoTypeBindingViews.ComparerSlotMemberId
                && (row is not ObjectMemberValue { classId: string } || row.valueType is not null))
            {
                return $"{RefusalSubject(row, slot)} is an authored comparer, so it must be a class row.";
            }
            Member? payload = slot;
            if (row.valueType is TypeBinding valueType)
            {
                string key = KeyOf(valueType) ?? valueType.Head?.Type.ToString() ?? valueType.Kind.ToString();
                string shown = NeoTypeNames.Of(valueType, data);
                BoxSlotKind kind = BoxSlotOf(slot);
                if (kind == BoxSlotKind.None)
                    return $"{RefusalSubject(row, slot)} has valueType '{shown}', but its member's type can't hold a box.";
                NeoBoxForm form = Box(valueType);
                string? refusal = form.Kind switch
                {
                    NeoBoxFormKind.Invalid => form.Invalid,
                    NeoBoxFormKind.Identity => kind == BoxSlotKind.Object
                        ? null
                        : $"holds '{shown}', but only an object slot holds a new object()",
                    NeoBoxFormKind.Comparer => ComparerSlotRefusal(slot, kind, key, shown, context),
                    _ => MemberBoxSlotRefusal(slot, kind, form.Member!, key, shown, context),
                };
                if (refusal is not null)
                    return $"{RefusalSubject(row, slot)} {refusal}.";
                payload = form.Member;
            }
            else if (slot is ObjectMember objectSlot && row is not NullMemberValue)
            {
                string? classId = (row as ObjectMemberValue)?.classId;
                string? refusal = objectSlot.objectType switch
                {
                    NeoObjectTypeKind.Type or NeoObjectTypeKind.CultureInfo or NeoObjectTypeKind.NumberFormatInfo => classId is null
                        ? null
                        : $"is a {objectSlot.objectType} slot and can't hold class \"{classId}\"",
                    _ when classId is null => "must name its value's type with a classId or valueType",
                    null => null,
                    _ => $"is a {objectSlot.objectType} slot and holds only boxes, not class \"{classId}\"",
                };
                if (refusal is not null)
                    return $"{RefusalSubject(row, slot)} {refusal}.";
            }
            if (row.comparer is not null && !HoldsCollectionComparer(payload))
                return $"{RefusalSubject(row, slot)} has a comparer, but only Set and Dictionary values carry one.";
            return null;
        }

        /// <summary>A refusal's subject, built only when refusing.</summary>
        private static string RefusalSubject(MemberValue row, Member slot) => $"value \"{row.id}\" ({slot.name})";

        /// <summary>The slots §1.3 lets hold a box, by what they admit.</summary>
        private enum BoxSlotKind
        {
            None,
            Object,
            ValueType,
            Enum,
            ClassTyped,
            InterfaceTyped,
        }

        private static BoxSlotKind BoxSlotOf(Member slot) => slot switch
        {
            ObjectMember { objectType: null } => BoxSlotKind.Object,
            ObjectMember { objectType: NeoObjectTypeKind.ValueType } => BoxSlotKind.ValueType,
            ObjectMember { objectType: NeoObjectTypeKind.Enum } => BoxSlotKind.Enum,
            // `Type`, `CultureInfo` and `NumberFormatInfo` store their payload.
            ObjectMember => BoxSlotKind.None,
            ClassMember { DeclaredInterfaceId: not null } => BoxSlotKind.InterfaceTyped,
            ClassMember => BoxSlotKind.ClassTyped,
            _ => BoxSlotKind.None,
        };

        /// <summary>A held comparer is a reference type of its public class (§1.3, §12.1).</summary>
        private string? ComparerSlotRefusal(Member slot, BoxSlotKind kind, string key, string shown, in NeoSlotContext context)
        {
            switch (kind)
            {
                case BoxSlotKind.ValueType:
                    return $"holds comparer '{shown}', but a ValueType slot holds only value types";
                case BoxSlotKind.Enum:
                    return $"holds comparer '{shown}', but an Enum slot holds only enums";
                case BoxSlotKind.InterfaceTyped:
                    {
                        if (InterfaceSlotRefusal((ClassMember)slot, shown, context, out ClosedInterfaceSlot closed) is string refusal)
                            return refusal;
                        NeoRuntimeTypeKeys.TryParse(key, out NeoRuntimeTypeKey parsed, out _);
                        // The comparer's T, when it mints (the server's `heldComparer`).
                        NeoRuntimeTypeKey? typeArgument = parsed.Arguments is { Length: > 0 } arguments
                            && BindingOfKey(arguments[0]) is TypeBinding argumentBinding
                            && NeoTypeBindingViews.PayloadView(argumentBinding, out _) is not null
                            ? arguments[0]
                            : null;
                        NeoClosedConstruction[]? constructions =
                            NeoSystemComparerClasses.InterimConstructions(NeoSystemComparerClasses.NameOf(parsed.Id!), typeArgument);
                        return constructions is null
                            ? $"holds '{shown}', which implements no system interface, so {WithArticle(closed.Text)} slot can't hold it"
                            : ConformanceRefusal(constructions, closed, shown, context);
                    }
                case BoxSlotKind.ClassTyped:
                    return ClassTypedComparerRefusal((ClassMember)slot, key, shown, context);
                default:
                    return null;
            }
        }

        /// <summary>
        /// A class-typed slot holds a comparer of exactly its class, closed
        /// over the slot's type arguments, compared invariantly as the
        /// server's <c>bindingsInvariantlyEqual</c>: a forwarded parameter
        /// equals no closed argument.
        /// </summary>
        private string? ClassTypedComparerRefusal(ClassMember slot, string key, string shown, in NeoSlotContext context)
        {
            string declared = slot.DeclaredClassId!;
            NeoRuntimeTypeKeys.TryParse(key, out NeoRuntimeTypeKey parsed, out _);
            if (parsed.Id != declared)
                return $"holds '{shown}', which isn't the slot's class \"{declared}\"";
            if (slot.classArguments is not { Count: > 0 } bindings)
                return null;
            List<GenericParamDeclaration> parameters = ParametersOf(NeoRuntimeTypeKeyKind.Class, declared);
            foreach (KeyValuePair<string, TypeBinding> binding in bindings)
            {
                int index = parameters.FindIndex(parameter => parameter.id == binding.Key);
                if (index < 0 || parsed.Arguments is null || index >= parsed.Arguments.Length)
                    return $"holds '{shown}', which doesn't bind the slot's parameter \"{binding.Key}\"";
                string? wanted = binding.Value.IsOpen
                    ? null
                    : TryDescribeBinding(binding.Value, NeoGenericResolution.EmptyEnv, out SlotType type) ? type.Key : null;
                if (wanted is null || NeoRuntimeTypeKeys.Spell(parsed.Arguments[index]) != wanted)
                    return $"holds '{shown}', whose type argument for \"{binding.Key}\" differs from the slot's";
            }
            return null;
        }

        /// <summary>§1.2: what an <c>object</c>, <c>ValueType</c>, <c>Enum</c> or interface slot admits.</summary>
        private string? MemberBoxSlotRefusal(Member slot, BoxSlotKind kind, Member held, string key, string shown, in NeoSlotContext context)
        {
            switch (kind)
            {
                case BoxSlotKind.ValueType:
                    return IsValueType(held) ? null : $"holds '{shown}', but a ValueType slot holds only value types";
                case BoxSlotKind.Enum:
                    return held is EnumMember { Selection: NeoMemberSelectionKind.Single }
                        ? null
                        : $"holds '{shown}', but an Enum slot holds only enums";
                case BoxSlotKind.ClassTyped:
                    return $"holds '{shown}', but a class-typed slot holds only its class's rows and comparers";
                case BoxSlotKind.InterfaceTyped:
                    {
                        if (InterfaceSlotRefusal((ClassMember)slot, shown, context, out ClosedInterfaceSlot closed) is string refusal)
                            return refusal;
                        if (IsBuiltInCollectionMember(held))
                            return $"holds '{shown}', a built-in collection, which an interface slot never stores (P125), so {WithArticle(closed.Text)} slot can't hold it";
                        NeoRuntimeTypeKeys.TryParse(key, out NeoRuntimeTypeKey parsed, out _);
                        if (ConformingKindOf(parsed) is not NeoConformingTypeKind conforming)
                            return $"holds '{shown}', which implements no system interface, so {WithArticle(closed.Text)} slot can't hold it";
                        return ConformanceRefusal(
                            NeoPrimitiveConformance.TableConstructions(conforming, parsed, closed.InterfaceId), closed, shown, context);
                    }
                default:
                    return null;
            }
        }

        /// <summary>An interface slot closed over its type arguments.</summary>
        private readonly struct ClosedInterfaceSlot
        {
            internal readonly string InterfaceId;
            /// <summary>Each parameter's argument in declaration order: null when the slot binds none, or it has no key.</summary>
            internal readonly SlotType?[] Arguments;
            /// <summary>The slot's type as the server's <c>formatTypeInfo</c> spells it: <c>IComparable&lt;int&gt;</c>.</summary>
            internal readonly string Text;

            internal ClosedInterfaceSlot(string interfaceId, SlotType?[] arguments, string text)
            {
                InterfaceId = interfaceId;
                Arguments = arguments;
                Text = text;
            }
        }

        /// <summary>
        /// §1.3: only system interfaces are implemented by values that box;
        /// an authored interface's implementers are classes, whose instances
        /// are rows. The slot's type arguments resolve in
        /// <paramref name="context"/>'s generic environment, and each must be
        /// determined. Worded as the server's <c>assertSystemInterfaceSlot</c>.
        /// </summary>
        private string? InterfaceSlotRefusal(ClassMember slot, string shown, in NeoSlotContext context, out ClosedInterfaceSlot closed)
        {
            closed = default;
            string interfaceId = slot.DeclaredInterfaceId!;
            if (!interfaceId.StartsWith("system_", StringComparison.Ordinal))
                return $"holds '{shown}', but authored interface \"{interfaceId}\" is implemented only by classes";
            string name = InterfaceName(interfaceId);
            List<GenericParamDeclaration> parameters = ParametersOf(NeoRuntimeTypeKeyKind.Interface, interfaceId);
            var arguments = new SlotType?[parameters.Count];
            var texts = new List<string>();
            if (slot.interfaceArguments is { Count: > 0 } bindings)
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> environment = context.Environment();
                foreach (KeyValuePair<string, TypeBinding> binding in bindings)
                {
                    if (!TryDescribeBinding(binding.Value, environment, out SlotType type))
                        return $"holds '{shown}', but the slot's {name} type argument \"{binding.Key}\" can't be determined";
                    int index = parameters.FindIndex(parameter => parameter.id == binding.Key);
                    if (index >= 0)
                        arguments[index] = type;
                    texts.Add(type.Text);
                }
            }
            closed = new ClosedInterfaceSlot(interfaceId, arguments, texts.Count == 0 ? name : $"{name}<{string.Join(", ", texts)}>");
            return null;
        }

        /// <summary>Why none of <paramref name="constructions"/> is the slot's closed interface, or null.</summary>
        private string? ConformanceRefusal(
            IReadOnlyList<NeoClosedConstruction> constructions,
            in ClosedInterfaceSlot closed,
            string shown,
            in NeoSlotContext context)
        {
            return Conforms(constructions, closed.InterfaceId, closed.Arguments, context, depth: 0)
                ? null
                : $"holds '{shown}', which doesn't implement {closed.Text}";
        }

        /// <summary>
        /// Whether one of <paramref name="constructions"/> is
        /// <paramref name="interfaceId"/> over <paramref name="wanted"/>: the
        /// server's <c>closedConstructionsConform</c>. Arguments match
        /// exactly (keys erase a reference type's <c>?</c>), except that a
        /// contravariant parameter closed over reference type <c>A</c> also
        /// serves any reference type that converts to <c>A</c> (§4.8).
        /// </summary>
        private bool Conforms(
            IReadOnlyList<NeoClosedConstruction> constructions,
            string interfaceId,
            SlotType?[] wanted,
            in NeoSlotContext context,
            int depth)
        {
            bool contravariant = NeoPrimitiveConformance.ContravariantInterfaceIds.Contains(interfaceId);
            foreach (NeoClosedConstruction row in constructions)
            {
                if (row.InterfaceId != interfaceId || row.Arguments.Length != wanted.Length)
                    continue;
                bool matches = true;
                for (int index = 0; index < wanted.Length && matches; index++)
                    matches = wanted[index] is SlotType argument && ArgumentServes(row.Arguments[index], argument, contravariant, context, depth);
                if (matches)
                    return true;
            }
            return false;
        }

        private bool ArgumentServes(NeoRuntimeTypeKey offered, SlotType wanted, bool contravariant, in NeoSlotContext context, int depth)
        {
            if (wanted.Key is string key && NeoRuntimeTypeKeys.Spell(offered) == key)
                return true;
            // `in T` reads target to source; value types never vary (§4.8).
            if (!contravariant || !wanted.IsReference || !IsReferenceKey(offered))
                return false;
            return wanted.Parsed is NeoRuntimeTypeKey source && Assignable(source, offered, context, depth + 1);
        }

        /// <summary>
        /// Whether a value of type <paramref name="from"/> is assignable to
        /// <paramref name="to"/>: the server's <c>isTypeInfoAssignable</c>
        /// over runtime type keys. A reference reads through to its target; a
        /// class converts along its chain, and to an interface its records
        /// construct with matching arguments; a primitive, enum or tuple
        /// converts to an interface the conformance table lists for it; and
        /// every value converts up C#'s built-in hierarchy. Keys erase a
        /// reference type's <c>?</c> at every depth, so this is the server's
        /// <c>NullabilityComparison.Erased</c>.
        /// </summary>
        private bool Assignable(NeoRuntimeTypeKey from, NeoRuntimeTypeKey to, in NeoSlotContext context, int depth)
        {
            if (depth > MaxTypeDepth)
                return false;
            if (from.Kind == NeoRuntimeTypeKeyKind.Nullable)
            {
                if (to.Kind != NeoRuntimeTypeKeyKind.Nullable)
                    return false;
                from = from.Arguments![0];
            }
            if (to.Kind == NeoRuntimeTypeKeyKind.Nullable)
                to = to.Arguments![0];
            if (IsReference(from))
            {
                // P112 §3.1: a reference fits a reference to the same target,
                // and otherwise converts by reading through one layer.
                if (IsReference(to) && NeoRuntimeTypeKeys.Spell(from.Arguments![0]) == NeoRuntimeTypeKeys.Spell(to.Arguments![0]))
                    return true;
                return Assignable(from.Arguments![0], to, context, depth + 1);
            }
            if (IsReference(to))
                return false;
            if (to.Kind == NeoRuntimeTypeKeyKind.Builtin && IsBuiltinObject(to))
                return ConvertsToBuiltinObject(from, to.Name!);
            switch (from.Kind, to.Kind)
            {
                case (NeoRuntimeTypeKeyKind.Tuple, NeoRuntimeTypeKeyKind.Tuple):
                    {
                        NeoRuntimeTypeKey[] elements = from.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
                        NeoRuntimeTypeKey[] targets = to.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
                        if (elements.Length != targets.Length)
                            return false;
                        for (int index = 0; index < elements.Length; index++)
                        {
                            if (!Assignable(elements[index], targets[index], context, depth + 1))
                                return false;
                        }
                        return true;
                    }
                case (NeoRuntimeTypeKeyKind.Builtin, NeoRuntimeTypeKeyKind.Builtin) when from.Name == "int" && to.Name == "float":
                    return true;
                case (NeoRuntimeTypeKeyKind.Class, NeoRuntimeTypeKeyKind.Class):
                    return from.Id == to.Id
                        ? NeoRuntimeTypeKeys.Spell(from) == NeoRuntimeTypeKeys.Spell(to)
                        : ConvertsToBaseClass(from, to);
                case (NeoRuntimeTypeKeyKind.Class, NeoRuntimeTypeKeyKind.Interface):
                case (NeoRuntimeTypeKeyKind.Interface, NeoRuntimeTypeKeyKind.Interface):
                    return ConstructsInterface(from, to, context);
                case (_, NeoRuntimeTypeKeyKind.Interface):
                    return TableConforms(from, to, context, depth);
                case (NeoRuntimeTypeKeyKind.Interface, _):
                    return false;
                default:
                    return NeoRuntimeTypeKeys.Spell(from) == NeoRuntimeTypeKeys.Spell(to);
            }
        }

        /// <summary>
        /// P127 §8.1: a primitive, enum or tuple boxes to the interfaces the
        /// conformance table lists for it. The server's <c>conformsThroughTable</c>.
        /// </summary>
        private bool TableConforms(NeoRuntimeTypeKey from, NeoRuntimeTypeKey to, in NeoSlotContext context, int depth)
        {
            if (ConformingKindOf(from) is not NeoConformingTypeKind kind)
                return false;
            List<GenericParamDeclaration> parameters = ParametersOf(NeoRuntimeTypeKeyKind.Interface, to.Id!);
            NeoRuntimeTypeKey[] arguments = to.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
            if (arguments.Length != parameters.Count)
                return false;
            var wanted = new SlotType?[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
                wanted[index] = SlotType.Of(arguments[index]);
            return Conforms(
                NeoPrimitiveConformance.TableConstructions(kind, from, to.Id!), to.Id!, wanted, context, depth + 1);
        }

        /// <summary>
        /// <see cref="Assignable(NeoRuntimeTypeKey, NeoRuntimeTypeKey, in NeoSlotContext, int)"/>
        /// over closed type infos: the server's <c>isTypeInfoAssignable</c>, so
        /// a class converts to its closed base (P127 §1.2). Annotated, as the
        /// server's default is: keys erase a nested reference type's <c>?</c>,
        /// so <see cref="KeepsNullability"/> checks it. Null when either type
        /// stays open, which only the runtime closes.
        /// </summary>
        internal bool? Assignable(NeoClient client, TypeInfo from, TypeInfo to)
        {
            if (ClosedKey(from) is not NeoRuntimeTypeKey fromKey || ClosedKey(to) is not NeoRuntimeTypeKey toKey)
                return null;
            return Assignable(fromKey, toKey, new NeoSlotContext(client), depth: 0) && KeepsNullability(client, from, to);
        }

        /// <summary>
        /// Given that the erased keys convert, whether every <c>?</c> the
        /// server's <c>NullabilityComparison.Annotated</c> arms compare matches
        /// too. A table conformance stays erased, as the server's is.
        /// </summary>
        private bool KeepsNullability(NeoClient client, TypeInfo from, TypeInfo to)
        {
            if (to.type == MemberKind.Unknown)
                return true;
            if (!from.required && to.required)
                return false;
            if (from.type == MemberKind.Reference && NeoReferenceTypes.TargetOf(from) is { } target)
            {
                if (to.type == MemberKind.Reference
                    && NeoReferenceTypes.TargetOf(to) is { } toTarget
                    && NeoReferenceTypes.InvariantlyEqual(target, toTarget))
                    return true;
                TypeInfo read = target.ShallowClone();
                read.required = from.required && target.required;
                return KeepsNullability(client, read, to);
            }
            switch (from.type, to.type)
            {
                case (_, MemberKind.Reference):
                    return false;
                case (MemberKind.Null, _):
                case (_, MemberKind.Object):
                case (MemberKind.Int, MemberKind.Float):
                    return true;
                case (MemberKind.Tuple, MemberKind.Tuple):
                    {
                        TupleTypeInfo? elements = TupleTypeInfo.From(from);
                        TupleTypeInfo? targets = TupleTypeInfo.From(to);
                        if (elements is null || targets is null || elements.elements.Length != targets.elements.Length)
                            return false;
                        for (int index = 0; index < elements.elements.Length; index++)
                        {
                            if (!KeepsNullability(client, elements.elements[index].typeInfo, targets.elements[index].typeInfo))
                                return false;
                        }
                        return true;
                    }
                case (MemberKind.Class, MemberKind.Class):
                    return NeoReferenceTypes.ClassIdOf(from) == NeoReferenceTypes.ClassIdOf(to)
                        ? NeoReferenceTypes.ShapesEqual(from, to)
                        : BaseArgumentsKeepNullability(client, from, to);
                case (MemberKind.Class, MemberKind.Interface):
                case (MemberKind.Interface, MemberKind.Interface):
                    return ConstructionKeepsNullability(client, from, to);
                case (_, MemberKind.Interface):
                    return true;
                default:
                    return NeoReferenceTypes.ShapesEqual(from, to);
            }
        }

        /// <summary>The arguments class <paramref name="from"/> passes its base <paramref name="to"/> are <paramref name="to"/>'s, nested <c>?</c> included. The server's <c>convertsToBaseClass</c>.</summary>
        private bool BaseArgumentsKeepNullability(NeoClient client, TypeInfo from, TypeInfo to)
        {
            string rowClassId = NeoReferenceTypes.ClassIdOf(from)!;
            if (OpenRowBaseArguments(client, rowClassId, NeoReferenceTypes.ClassIdOf(to)!) is not { } passed)
                return false;
            if (NeoReferenceTypes.TypeArgumentsOf(to) is not { } expected)
                return true;
            Dictionary<string, TypeInfo> arguments = NeoReferenceTypes.TypeArgumentsOf(from) ?? EmptyTypeArguments;
            foreach (KeyValuePair<string, TypeInfo> pair in expected)
            {
                // A binding with no type info here keeps the erased result.
                if (!passed.TryGetValue(pair.Key, out TypeInfo? actual))
                    continue;
                if (!NeoReferenceTypes.InvariantlyEqual(SubstituteParams(actual, rowClassId, arguments), pair.Value))
                    return false;
            }
            return true;
        }

        /// <summary>The arguments <paramref name="from"/> implements interface <paramref name="to"/> with are <paramref name="to"/>'s, nested <c>?</c> included. The server's <c>constructsInterface</c>.</summary>
        private bool ConstructionKeepsNullability(NeoClient client, TypeInfo from, TypeInfo to)
        {
            string interfaceId = NeoReferenceTypes.InterfaceIdOf(to)!;
            bool found = from.type == MemberKind.Class
                ? client.InterfaceImplementers.TryGetConstruction(NeoReferenceTypes.ClassIdOf(from)!, interfaceId, out IReadOnlyDictionary<string, TypeBinding> construction)
                : client.InterfaceImplementers.InterfaceClosure(NeoReferenceTypes.InterfaceIdOf(from)!).TryGetValue(interfaceId, out construction!);
            if (!found)
                return false;
            if (NeoReferenceTypes.TypeArgumentsOf(to) is not { } expected)
                return true;
            Dictionary<string, TypeInfo>? holderArguments = NeoReferenceTypes.TypeArgumentsOf(from);
            foreach (KeyValuePair<string, TypeInfo> pair in expected)
            {
                if (!construction.TryGetValue(pair.Key, out TypeBinding? binding))
                    return false;
                TypeInfo? actual = binding.IsOpen
                    ? holderArguments?.GetValueOrDefault(binding.Param!)
                    : client.TryGetMember(NeoTypeBindingViews.Id(binding), out Member? member) ? BindingTypeInfo(client, member) : null;
                // A binding with no type info here keeps the erased result.
                if (actual is null)
                    continue;
                if (!NeoReferenceTypes.InvariantlyEqual(actual, pair.Value))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// <paramref name="typeInfo"/>'s key, or null when it stays open. A
        /// class named without type arguments keys bare: as a target it
        /// compares no arguments, and it passes none up its chain.
        /// </summary>
        private NeoRuntimeTypeKey? ClosedKey(TypeInfo typeInfo)
        {
            if (typeInfo is ClassTypeInfo { typeArguments: null or { Count: 0 } } bare)
            {
                string spelled = "c:" + bare.classId + (bare.required ? "" : "?");
                return NeoRuntimeTypeKeys.TryParse(spelled, out NeoRuntimeTypeKey key, out _) ? key : null;
            }
            try
            {
                return DescribeTypeInfo(typeInfo, NeoGenericResolution.EmptyEnv, depth: 0).Parsed;
            }
            catch (OpenTypeException)
            {
                return null;
            }
        }

        private static bool IsReference(NeoRuntimeTypeKey key) =>
            key.Kind == NeoRuntimeTypeKeyKind.BuiltinConstruction && key.Name == "Reference";

        /// <summary><c>object</c>, <c>ValueType</c>, <c>Enum</c>, <c>Type</c>, <c>CultureInfo</c> or <c>NumberFormatInfo</c>.</summary>
        private static bool IsBuiltinObject(NeoRuntimeTypeKey key) =>
            key.Kind == NeoRuntimeTypeKeyKind.Builtin && Array.IndexOf(NeoRuntimeTypeKeys.P127Builtins, key.Name) >= 0;

        /// <summary>
        /// P127 §1.2, §2.1: C#'s <c>Enum : ValueType : object</c>, with
        /// <c>Type</c>, <c>CultureInfo</c>, <c>NumberFormatInfo</c> and the
        /// built-in comparers deriving straight from <c>object</c>. Value
        /// types box to <c>ValueType</c>, enums to <c>Enum</c>, and every
        /// value to <c>object</c>; a <c>NeoAction</c> isn't a value. The
        /// server's <c>convertsToBuiltInObject</c>.
        /// </summary>
        private static bool ConvertsToBuiltinObject(NeoRuntimeTypeKey from, string to)
        {
            if (IsBuiltinObject(from))
                return to == ObjectKey || from.Name == to || (from.Name == "Enum" && to == "ValueType");
            return to switch
            {
                ObjectKey => from.Name != NeoAction,
                "ValueType" => IsValueKey(from),
                "Enum" => from.Kind == NeoRuntimeTypeKeyKind.Enum,
                _ => false,
            };
        }

        private static readonly HashSet<string> BuiltinValueTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "int",
            "float",
            "decimal",
            "bool",
            "Vector2",
            "Vector2Int",
            "Vector3",
            "Vector3Int",
            "Color",
        };

        /// <summary>§2's value types: primitives, vectors, <c>Color</c>, enums and tuples.</summary>
        private static bool IsValueKey(NeoRuntimeTypeKey key) => key.Kind switch
        {
            NeoRuntimeTypeKeyKind.Builtin => BuiltinValueTypes.Contains(key.Name!),
            NeoRuntimeTypeKeyKind.Enum or NeoRuntimeTypeKeyKind.Tuple => true,
            _ => false,
        };

        /// <summary>
        /// C#'s class conversion: <paramref name="to"/> is an ancestor of
        /// <paramref name="from"/>, and the arguments <paramref name="from"/>
        /// passes up its extends chain to it are <paramref name="to"/>'s.
        /// Walks the chain once; an open target compares no arguments. The
        /// server's <c>convertsToBaseClass</c>.
        /// </summary>
        private bool ConvertsToBaseClass(NeoRuntimeTypeKey from, NeoRuntimeTypeKey to)
        {
            if (data?.classes is not { } classes)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string classId = from.Id!;
            NeoRuntimeTypeKey?[] arguments = from.Arguments ?? Array.Empty<NeoRuntimeTypeKey?>();
            while (seen.Add(classId)
                && classes.TryGetValue(classId, out NeoSchemaClass? schemaClass)
                && schemaClass.extendsClassId is string baseId)
            {
                NeoRuntimeTypeKey?[] passed = BaseClassArguments(schemaClass, arguments, baseId);
                if (baseId == to.Id)
                {
                    NeoRuntimeTypeKey[] expected = to.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
                    if (expected.Length == 0)
                        return true;
                    if (expected.Length != passed.Length)
                        return false;
                    for (int index = 0; index < expected.Length; index++)
                    {
                        if (passed[index] is not NeoRuntimeTypeKey actual
                            || NeoRuntimeTypeKeys.Spell(actual) != NeoRuntimeTypeKeys.Spell(expected[index]))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                classId = baseId;
                arguments = passed;
            }
            return false;
        }

        private static readonly Dictionary<string, TypeBinding> NoBindings = new Dictionary<string, TypeBinding>();
        private static readonly Dictionary<string, TypeInfo> EmptyTypeArguments = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);

        /// <summary>
        /// Drops the per-class-pair memo after a tooling seam edits the
        /// schema in place. An export's schema is otherwise fixed once loaded:
        /// partitions add values, and a new export is a new
        /// <see cref="ProjectData"/>.
        /// </summary>
        internal void ForgetClassPairs()
        {
            openRowBaseArguments.Clear();
        }

        /// <summary>
        /// P127 §1.2. Whether a row of open <paramref name="rowClassId"/>
        /// converts to <paramref name="target"/>, inferring its params as C#
        /// does: each binds to the first type in its place among what the row
        /// passes the target's class, and the substituted arguments must then
        /// equal the target's. So <c>ListBox&lt;T&gt; : Box&lt;List&lt;T&gt;&gt;</c>
        /// fits <c>Box&lt;List&lt;Seed&gt;&gt;</c> but not <c>Box&lt;Seed&gt;</c>,
        /// and <c>Twin&lt;T&gt; : Box2&lt;T, T&gt;</c> fits <c>Box2&lt;Seed, Seed&gt;</c>
        /// but not <c>Box2&lt;Seed, Tool&gt;</c>. The server's
        /// <c>openRowConvertsToClass</c>.
        /// </summary>
        internal bool OpenRowConvertsToClass(NeoClient client, string rowClassId, ClassTypeInfo target)
        {
            if (target.classId == rowClassId)
                return true;
            return UnifyRowParams(client, rowClassId, target.classId, target.typeArguments) is not null;
        }

        /// <summary>
        /// Binds <paramref name="rowClassId"/>'s params in what it passes its
        /// base <paramref name="baseClassId"/> to that base's
        /// <paramref name="expected"/> arguments, each to the first type in its
        /// place. Null when the base isn't one, or when any substituted
        /// argument differs. The server's <c>unifyRowParams</c>.
        /// </summary>
        internal Dictionary<string, TypeInfo>? UnifyRowParams(
            NeoClient client,
            string rowClassId,
            string baseClassId,
            Dictionary<string, TypeInfo>? expected)
        {
            if (OpenRowBaseArguments(client, rowClassId, baseClassId) is not { } passed)
                return null;
            var bound = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
            if (expected is null)
                return bound;
            foreach (KeyValuePair<string, TypeInfo> pair in expected)
            {
                if (!passed.TryGetValue(pair.Key, out TypeInfo? actual))
                    return null;
                BindRowParams(actual, pair.Value, rowClassId, bound);
            }
            // Each row param keeps its first binding, so a second, different one fails.
            foreach (KeyValuePair<string, TypeInfo> pair in expected)
            {
                TypeInfo actual = SubstituteParams(passed[pair.Key], rowClassId, bound);
                if (!NeoReferenceTypes.InvariantlyEqual(actual, pair.Value))
                    return null;
            }
            return bound;
        }

        /// <summary>
        /// The arguments <paramref name="rowClassId"/>, its own params left
        /// open, passes its base <paramref name="baseClassId"/>, keyed by the
        /// base's params. Memoized per class pair, so judging a row is
        /// O(params). Null when <paramref name="baseClassId"/> isn't a base.
        /// The server's <c>openRowBaseArguments</c>.
        /// </summary>
        private Dictionary<string, TypeInfo>? OpenRowBaseArguments(NeoClient client, string rowClassId, string baseClassId)
        {
            if (openRowBaseArguments.TryGetValue((rowClassId, baseClassId), out Dictionary<string, TypeInfo>? cached))
                return cached;
            Dictionary<string, TypeInfo>? result = null;
            if (data?.classes is { } classes)
            {
                var arguments = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                foreach (GenericParamDeclaration parameter in ParametersOf(NeoRuntimeTypeKeyKind.Class, rowClassId))
                {
                    arguments[parameter.id] = new GenericTypeInfo
                    {
                        type = MemberKind.Generic,
                        required = true,
                        ownerClassId = rowClassId,
                        genericParamId = parameter.id,
                    };
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                string classId = rowClassId;
                while (seen.Add(classId)
                    && classes.TryGetValue(classId, out NeoSchemaClass? schemaClass)
                    && schemaClass.extendsClassId is string baseId)
                {
                    var passed = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, TypeBinding> binding in schemaClass.extendsGenericBindings ?? NoBindings)
                    {
                        TypeInfo? argument = binding.Value.IsOpen
                            ? arguments.GetValueOrDefault(binding.Value.Param!)
                            : client.TryGetMember(NeoTypeBindingViews.Id(binding.Value), out Member? member)
                                ? BindingTypeInfo(client, member)
                                : null;
                        if (argument is not null)
                            passed[binding.Key] = binding.Value.IsOpen ? argument : SubstituteParams(argument, classId, arguments);
                    }
                    if (baseId == baseClassId)
                    {
                        result = passed;
                        break;
                    }
                    arguments = passed;
                    classId = baseId;
                }
            }
            openRowBaseArguments[(rowClassId, baseClassId)] = result;
            return result;
        }

        /// <summary>
        /// The type a base binding member passes: its slot type, an
        /// <c>object</c> member's type, or a delegate's or action's
        /// signature, as the server's <c>openBindingTypeInfo</c> spells a
        /// delegate.
        /// </summary>
        private static TypeInfo? BindingTypeInfo(NeoClient client, Member member)
        {
            bool required = member.Requirement == NeoMemberRequirementKind.Required;
            switch (member)
            {
                case ObjectMember objectMember:
                    return new ObjectTypeInfo
                    {
                        type = MemberKind.Object,
                        required = required,
                        objectType = objectMember.objectType,
                    };
                case DelegateMember callable:
                    return new DelegateTypeInfo
                    {
                        type = MemberKind.NSDelegate,
                        required = required,
                        returnTypeInfo = callable.returnTypeInfo,
                        argumentTypes = PlainArguments(callable.argumentTypes),
                    };
                case ActionMember action:
                    // An action is never nullable (P62 §2.1).
                    return new ActionTypeInfo
                    {
                        type = MemberKind.NSAction,
                        required = true,
                        argumentTypes = PlainArguments(action.argumentTypes),
                    };
                default:
                    return NeoReferenceTypes.SlotTypeInfo(client, member);
            }
        }

        internal static TypeInfo[] PlainArguments(FunctionArgumentTypeInfo[] argumentTypes)
        {
            var plain = new TypeInfo[argumentTypes.Length];
            for (int index = 0; index < plain.Length; index++)
                plain[index] = Plain(argumentTypes[index]);
            return plain;
        }

        /// <summary>A callable's parameter as the unnamed type it holds, keeping a class param's owner.</summary>
        internal static TypeInfo Plain(TypeInfo typeInfo)
        {
            if (typeInfo is not FunctionArgumentTypeInfo argument)
                return typeInfo;
            if (argument.type != MemberKind.Generic || argument.index is not null)
                return Typed(argument);
            return new GenericTypeInfo
            {
                type = MemberKind.Generic,
                required = argument.required,
                ownerClassId = argument.ownerClassId,
                ownerInterfaceId = argument.ownerInterfaceId,
                genericParamId = argument.genericParamId!,
            };
        }

        /// <summary>
        /// The declared type an IR type position spells. The IR spells a
        /// <c>Set&lt;T&gt;</c> position as a Lookup bound to no collection,
        /// so this reads that arm, at any depth, as the Set it declares
        /// (P127 §12.1). A Lookup bound to a collection is a read type no
        /// declared type represents, so it throws. A callable's parameter
        /// reads as its <see cref="Plain"/> type. Returns that type itself
        /// when nothing changes.
        /// </summary>
        internal static TypeInfo Declared(TypeInfo typeInfo)
        {
            typeInfo = Plain(typeInfo);
            switch (typeInfo)
            {
                case LookupTypeInfo lookup:
                    if (lookup.collectionMemberId is not null)
                        throw new InvalidOperationException("A Lookup bound to a collection is a read type, so it declares no type.");
                    return new CollectionTypeInfo
                    {
                        type = MemberKind.Set,
                        required = lookup.required,
                        entryTypeInfo = Declared(lookup.entryTypeInfo),
                    };
                case ClassTypeInfo { typeArguments: { Count: > 0 } arguments } classType:
                    {
                        Dictionary<string, TypeInfo>? declared = DeclaredArguments(arguments);
                        if (declared is null)
                            return typeInfo;
                        var copy = (ClassTypeInfo)classType.ShallowClone();
                        copy.typeArguments = declared;
                        return copy;
                    }
                case InterfaceTypeInfo { typeArguments: { Count: > 0 } arguments } interfaceType:
                    {
                        Dictionary<string, TypeInfo>? declared = DeclaredArguments(arguments);
                        if (declared is null)
                            return typeInfo;
                        var copy = (InterfaceTypeInfo)interfaceType.ShallowClone();
                        copy.typeArguments = declared;
                        return copy;
                    }
                case CollectionTypeInfo collection:
                    {
                        TypeInfo entry = Declared(collection.entryTypeInfo);
                        if (ReferenceEquals(entry, collection.entryTypeInfo))
                            return typeInfo;
                        var copy = (CollectionTypeInfo)collection.ShallowClone();
                        copy.entryTypeInfo = entry;
                        return copy;
                    }
                case ReferenceTypeInfo reference:
                    {
                        TypeInfo target = Declared(reference.targetTypeInfo);
                        if (ReferenceEquals(target, reference.targetTypeInfo))
                            return typeInfo;
                        var copy = (ReferenceTypeInfo)reference.ShallowClone();
                        copy.targetTypeInfo = target;
                        return copy;
                    }
                case TupleTypeInfo tuple:
                    {
                        TupleElementTypeInfo[]? elements = null;
                        for (int index = 0; index < tuple.elements.Length; index++)
                        {
                            TupleElementTypeInfo element = tuple.elements[index];
                            TypeInfo declared = Declared(element.typeInfo);
                            if (ReferenceEquals(declared, element.typeInfo))
                                continue;
                            elements ??= (TupleElementTypeInfo[])tuple.elements.Clone();
                            elements[index] = new TupleElementTypeInfo { typeInfo = declared, name = element.name };
                        }
                        if (elements is null)
                            return typeInfo;
                        var copy = (TupleTypeInfo)tuple.ShallowClone();
                        copy.elements = elements;
                        return copy;
                    }
                case DelegateTypeInfo delegateType:
                    {
                        TypeInfo returnType = Declared(delegateType.returnTypeInfo);
                        TypeInfo[]? arguments = DeclaredPositions(delegateType.argumentTypes);
                        if (arguments is null && ReferenceEquals(returnType, delegateType.returnTypeInfo))
                            return typeInfo;
                        var copy = (DelegateTypeInfo)delegateType.ShallowClone();
                        copy.returnTypeInfo = returnType;
                        copy.argumentTypes = arguments ?? delegateType.argumentTypes;
                        return copy;
                    }
                case ActionTypeInfo actionType:
                    {
                        TypeInfo[]? arguments = DeclaredPositions(actionType.argumentTypes);
                        if (arguments is null)
                            return typeInfo;
                        var copy = (ActionTypeInfo)actionType.ShallowClone();
                        copy.argumentTypes = arguments;
                        return copy;
                    }
                default:
                    return typeInfo;
            }
        }

        /// <summary><see cref="Declared"/> over each type argument; null when none changes.</summary>
        private static Dictionary<string, TypeInfo>? DeclaredArguments(Dictionary<string, TypeInfo> arguments)
        {
            Dictionary<string, TypeInfo>? declared = null;
            foreach (KeyValuePair<string, TypeInfo> pair in arguments)
            {
                TypeInfo argument = Declared(pair.Value);
                if (ReferenceEquals(argument, pair.Value))
                    continue;
                declared ??= new Dictionary<string, TypeInfo>(arguments, StringComparer.Ordinal);
                declared[pair.Key] = argument;
            }
            return declared;
        }

        /// <summary><see cref="Declared"/> over each position; null when none changes.</summary>
        private static TypeInfo[]? DeclaredPositions(TypeInfo[] positions)
        {
            TypeInfo[]? declared = null;
            for (int index = 0; index < positions.Length; index++)
            {
                TypeInfo position = Declared(positions[index]);
                if (ReferenceEquals(position, positions[index]))
                    continue;
                // A covariant array (built as, say, LookupTypeInfo[]) refuses another kind.
                if (declared is null)
                {
                    declared = new TypeInfo[positions.Length];
                    Array.Copy(positions, declared, positions.Length);
                }
                declared[index] = position;
            }
            return declared;
        }

        /// <summary>
        /// Binds each of <paramref name="rowClassId"/>'s params
        /// <paramref name="pattern"/> names to the part of
        /// <paramref name="target"/> in the same place, keeping the first
        /// binding. A shape mismatch binds nothing there; the substituted
        /// comparison then refuses it. The server's <c>bindRowParams</c>.
        /// </summary>
        private static void BindRowParams(TypeInfo pattern, TypeInfo target, string rowClassId, Dictionary<string, TypeInfo> bound)
        {
            switch (pattern)
            {
                case GenericTypeInfo generic:
                    if (generic.ownerClassId == rowClassId)
                        bound.TryAdd(generic.genericParamId, target);
                    return;
                case CollectionTypeInfo collection:
                    if (target is CollectionTypeInfo otherCollection && otherCollection.type == collection.type)
                        BindRowParams(collection.entryTypeInfo, otherCollection.entryTypeInfo, rowClassId, bound);
                    return;
                case LookupTypeInfo lookup:
                    if (target is LookupTypeInfo otherLookup)
                        BindRowParams(lookup.entryTypeInfo, otherLookup.entryTypeInfo, rowClassId, bound);
                    return;
                case ReferenceTypeInfo reference:
                    if (target is ReferenceTypeInfo otherReference)
                        BindRowParams(reference.targetTypeInfo, otherReference.targetTypeInfo, rowClassId, bound);
                    return;
                case TupleTypeInfo tuple:
                    if (target is not TupleTypeInfo otherTuple)
                        return;
                    for (int index = 0; index < tuple.elements.Length && index < otherTuple.elements.Length; index++)
                        BindRowParams(tuple.elements[index].typeInfo, otherTuple.elements[index].typeInfo, rowClassId, bound);
                    return;
                case ClassTypeInfo classType:
                    if (target is ClassTypeInfo otherClass && otherClass.classId == classType.classId)
                        BindTypeArguments(classType.typeArguments, otherClass.typeArguments, rowClassId, bound);
                    return;
                case InterfaceTypeInfo interfaceType:
                    if (target is InterfaceTypeInfo otherInterface && otherInterface.interfaceId == interfaceType.interfaceId)
                        BindTypeArguments(interfaceType.typeArguments, otherInterface.typeArguments, rowClassId, bound);
                    return;
                case DelegateTypeInfo callable:
                    if (target is not DelegateTypeInfo otherCallable)
                        return;
                    // A void return on either side binds nothing, so a param
                    // against void stays a param and the comparison refuses it.
                    if (callable.returnTypeInfo.type != MemberKind.Void && otherCallable.returnTypeInfo.type != MemberKind.Void)
                        BindRowParams(callable.returnTypeInfo, otherCallable.returnTypeInfo, rowClassId, bound);
                    BindArgumentTypes(callable.argumentTypes, otherCallable.argumentTypes, rowClassId, bound);
                    return;
                case ActionTypeInfo action:
                    if (target is ActionTypeInfo otherAction)
                        BindArgumentTypes(action.argumentTypes, otherAction.argumentTypes, rowClassId, bound);
                    return;
            }
        }

        private static void BindArgumentTypes(TypeInfo[] pattern, TypeInfo[] target, string rowClassId, Dictionary<string, TypeInfo> bound)
        {
            for (int index = 0; index < pattern.Length && index < target.Length; index++)
                BindRowParams(Plain(pattern[index]), Plain(target[index]), rowClassId, bound);
        }

        private static void BindTypeArguments(
            Dictionary<string, TypeInfo>? pattern,
            Dictionary<string, TypeInfo>? target,
            string rowClassId,
            Dictionary<string, TypeInfo> bound)
        {
            if (pattern is null || target is null)
                return;
            foreach (KeyValuePair<string, TypeInfo> pair in pattern)
            {
                if (target.TryGetValue(pair.Key, out TypeInfo? other))
                    BindRowParams(pair.Value, other, rowClassId, bound);
            }
        }

        /// <summary><paramref name="typeInfo"/> with each of <paramref name="ownerClassId"/>'s params in <paramref name="arguments"/> replaced.</summary>
        private static TypeInfo SubstituteParams(TypeInfo typeInfo, string ownerClassId, Dictionary<string, TypeInfo> arguments)
        {
            switch (typeInfo)
            {
                case GenericTypeInfo generic:
                    return generic.ownerClassId == ownerClassId && arguments.TryGetValue(generic.genericParamId, out TypeInfo? argument)
                        ? argument
                        : generic;
                case CollectionTypeInfo collection:
                    {
                        var substituted = (CollectionTypeInfo)collection.ShallowClone();
                        substituted.entryTypeInfo = SubstituteParams(collection.entryTypeInfo, ownerClassId, arguments);
                        return substituted;
                    }
                case LookupTypeInfo lookup:
                    {
                        var substituted = (LookupTypeInfo)lookup.ShallowClone();
                        substituted.entryTypeInfo = SubstituteParams(lookup.entryTypeInfo, ownerClassId, arguments);
                        return substituted;
                    }
                case ReferenceTypeInfo reference:
                    {
                        var substituted = (ReferenceTypeInfo)reference.ShallowClone();
                        substituted.targetTypeInfo = SubstituteParams(reference.targetTypeInfo, ownerClassId, arguments);
                        return substituted;
                    }
                case TupleTypeInfo tuple:
                    {
                        var substituted = (TupleTypeInfo)tuple.ShallowClone();
                        substituted.elements = TupleTypeInfo.MapElements(tuple.elements, element => SubstituteParams(element, ownerClassId, arguments));
                        return substituted;
                    }
                case ClassTypeInfo { typeArguments: { } classArguments } classType:
                    {
                        var substituted = (ClassTypeInfo)classType.ShallowClone();
                        substituted.typeArguments = SubstituteArguments(classArguments, ownerClassId, arguments);
                        return substituted;
                    }
                case InterfaceTypeInfo { typeArguments: { } interfaceArguments } interfaceType:
                    {
                        var substituted = (InterfaceTypeInfo)interfaceType.ShallowClone();
                        substituted.typeArguments = SubstituteArguments(interfaceArguments, ownerClassId, arguments);
                        return substituted;
                    }
                case DelegateTypeInfo callable:
                    {
                        var substituted = (DelegateTypeInfo)callable.ShallowClone();
                        substituted.returnTypeInfo = SubstituteParams(callable.returnTypeInfo, ownerClassId, arguments);
                        substituted.argumentTypes = SubstituteArgumentTypes(callable.argumentTypes, ownerClassId, arguments);
                        return substituted;
                    }
                case ActionTypeInfo action:
                    {
                        var substituted = (ActionTypeInfo)action.ShallowClone();
                        substituted.argumentTypes = SubstituteArgumentTypes(action.argumentTypes, ownerClassId, arguments);
                        return substituted;
                    }
                default:
                    return typeInfo;
            }
        }

        private static TypeInfo[] SubstituteArgumentTypes(TypeInfo[] argumentTypes, string ownerClassId, Dictionary<string, TypeInfo> arguments)
        {
            var substituted = new TypeInfo[argumentTypes.Length];
            for (int index = 0; index < argumentTypes.Length; index++)
                substituted[index] = SubstituteParams(Plain(argumentTypes[index]), ownerClassId, arguments);
            return substituted;
        }

        private static Dictionary<string, TypeInfo> SubstituteArguments(
            Dictionary<string, TypeInfo> typeArguments,
            string ownerClassId,
            Dictionary<string, TypeInfo> arguments)
        {
            var substituted = new Dictionary<string, TypeInfo>(typeArguments.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeInfo> pair in typeArguments)
                substituted[pair.Key] = SubstituteParams(pair.Value, ownerClassId, arguments);
            return substituted;
        }

        /// <summary>
        /// The arguments <paramref name="schemaClass"/>, closed by
        /// <paramref name="arguments"/>, passes to <paramref name="baseId"/>,
        /// in the base's parameter order; null where one stays open.
        /// </summary>
        private NeoRuntimeTypeKey?[] BaseClassArguments(NeoSchemaClass schemaClass, NeoRuntimeTypeKey?[] arguments, string baseId)
        {
            List<GenericParamDeclaration> parameters = ParametersOf(NeoRuntimeTypeKeyKind.Class, schemaClass.id);
            List<GenericParamDeclaration> baseParameters = ParametersOf(NeoRuntimeTypeKeyKind.Class, baseId);
            var passed = new NeoRuntimeTypeKey?[baseParameters.Count];
            Dictionary<string, NeoGenericEnvEntry>? environment = null;
            for (int index = 0; index < passed.Length; index++)
            {
                if (schemaClass.extendsGenericBindings?.TryGetValue(baseParameters[index].id, out TypeBinding? binding) != true)
                    continue;
                if (binding!.IsOpen)
                {
                    int position = parameters.FindIndex(parameter => parameter.id == binding.Param);
                    passed[index] = position >= 0 && position < arguments.Length ? arguments[position] : null;
                    continue;
                }
                environment ??= ArgumentEnvironment(parameters, arguments);
                passed[index] = TryDescribeBinding(binding, environment, out SlotType type) ? type.Parsed : null;
            }
            return passed;
        }

        /// <summary>Each parameter bound to its argument key's binding view, or unbound.</summary>
        private Dictionary<string, NeoGenericEnvEntry> ArgumentEnvironment(List<GenericParamDeclaration> parameters, NeoRuntimeTypeKey?[] arguments)
        {
            var environment = new Dictionary<string, NeoGenericEnvEntry>(StringComparer.Ordinal);
            for (int index = 0; index < parameters.Count; index++)
            {
                Member? view = index < arguments.Length
                    && arguments[index] is NeoRuntimeTypeKey argument
                    && BindingOfKey(argument) is TypeBinding binding
                    ? NeoTypeBindingViews.TryView(binding, out _)
                    : null;
                environment[parameters[index].id] = view is not null
                    ? NeoGenericEnvEntry.Bound(view.id)
                    : NeoGenericEnvEntry.Unbound(parameters[index].id);
            }
            return environment;
        }

        /// <summary>
        /// P113 §2.4: class or interface <paramref name="holder"/>
        /// implements or extends <paramref name="to"/>'s interface with
        /// <paramref name="to"/>'s arguments. A forward in its construction
        /// reads the holder's own type argument. The server's
        /// <c>constructsInterface</c>.
        /// </summary>
        private bool ConstructsInterface(NeoRuntimeTypeKey holder, NeoRuntimeTypeKey to, in NeoSlotContext context)
        {
            if (context.Client is not NeoClient client)
                return false;
            bool found = holder.Kind == NeoRuntimeTypeKeyKind.Class
                ? client.InterfaceImplementers.TryGetConstruction(holder.Id!, to.Id!, out IReadOnlyDictionary<string, TypeBinding> construction)
                : client.InterfaceImplementers.InterfaceClosure(holder.Id!).TryGetValue(to.Id!, out construction!);
            if (!found)
                return false;
            NeoRuntimeTypeKey[] expected = to.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
            List<GenericParamDeclaration> positions = ParametersOf(NeoRuntimeTypeKeyKind.Interface, to.Id!);
            List<GenericParamDeclaration> holderParameters = ParametersOf(holder.Kind, holder.Id!);
            for (int index = 0; index < expected.Length && index < positions.Count; index++)
            {
                if (!construction.TryGetValue(positions[index].id, out TypeBinding? binding))
                    return false;
                string? actual;
                if (binding.IsOpen)
                {
                    int position = holderParameters.FindIndex(parameter => parameter.id == binding.Param);
                    actual = position >= 0 && holder.Arguments is { } holderArguments && position < holderArguments.Length
                        ? NeoRuntimeTypeKeys.Spell(holderArguments[position])
                        : null;
                }
                else
                {
                    actual = TryDescribeBinding(binding, NeoGenericResolution.EmptyEnv, out SlotType type) ? type.Key : null;
                }
                if (actual is null || actual != NeoRuntimeTypeKeys.Spell(expected[index]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// §9.5: a Set or Dictionary, including the multi-select Enum, Lookup
        /// and DialogueLookup P108 lowers a <c>Set</c> to. The TypeScript
        /// <c>holdsCollectionComparer</c>.
        /// </summary>
        private static bool HoldsCollectionComparer(Member? member) => member switch
        {
            SetMember or DictionaryMember => true,
            EnumMember enumMember => enumMember.Selection == NeoMemberSelectionKind.Multi,
            LookupMember lookup => lookup.Selection == NeoMemberSelectionKind.Multi,
            DialogueLookupMember dialogue => dialogue.Selection == NeoMemberSelectionKind.Multi,
            _ => false,
        };

        /// <summary>
        /// P125: a built-in <c>List</c>, <c>Set</c> or <c>Dictionary</c>,
        /// judged by the member's shape, so <c>Set&lt;e:X&gt;</c>,
        /// <c>Set&lt;Reference&lt;T&gt;&gt;</c> and
        /// <c>Set&lt;NeoDialogueReference&gt;</c> count. The TypeScript
        /// <c>isBuiltInCollectionMember</c>.
        /// </summary>
        private static bool IsBuiltInCollectionMember(Member member) => member is ListMember || HoldsCollectionComparer(member);

        /// <summary>Every type but §2's value types; an open type parameter is neither.</summary>
        private static bool IsReferenceKey(NeoRuntimeTypeKey key) => key.Kind switch
        {
            NeoRuntimeTypeKeyKind.Builtin => !BuiltinValueTypes.Contains(key.Name!),
            NeoRuntimeTypeKeyKind.Enum or NeoRuntimeTypeKeyKind.Tuple or NeoRuntimeTypeKeyKind.Nullable => false,
            NeoRuntimeTypeKeyKind.Definition or NeoRuntimeTypeKeyKind.Parameter => false,
            _ => true,
        };

        /// <summary>
        /// The conformance-table row (§8.1) a primitive, vector, enum, tuple
        /// or culture value conforms through, or null for any other type. A
        /// multi-select enum is a <c>Set</c>.
        /// </summary>
        private static NeoConformingTypeKind? ConformingKindOf(NeoRuntimeTypeKey key)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Enum:
                    return NeoConformingTypeKind.Enum;
                case NeoRuntimeTypeKeyKind.Tuple:
                    return NeoConformingTypeKind.Tuple;
                case NeoRuntimeTypeKeyKind.Builtin:
                    return key.Name switch
                    {
                        "int" => NeoConformingTypeKind.Int,
                        "float" => NeoConformingTypeKind.Float,
                        "decimal" => NeoConformingTypeKind.Decimal,
                        "bool" => NeoConformingTypeKind.Bool,
                        "string" => NeoConformingTypeKind.String,
                        "Vector2" => NeoConformingTypeKind.Vector2,
                        "Vector2Int" => NeoConformingTypeKind.Vector2Int,
                        "Vector3" => NeoConformingTypeKind.Vector3,
                        "Vector3Int" => NeoConformingTypeKind.Vector3Int,
                        "Color" => NeoConformingTypeKind.Color,
                        "CultureInfo" => NeoConformingTypeKind.CultureInfo,
                        "NumberFormatInfo" => NeoConformingTypeKind.NumberFormatInfo,
                        _ => null,
                    };
                default:
                    return null;
            }
        }

        private const int MaxTypeDepth = 32;

        /// <summary>
        /// A closed type a slot names: its §5.3 runtime type key, null when
        /// it has none, and its text as the server's <c>formatTypeInfo</c>
        /// prints the type argument.
        /// </summary>
        private readonly struct SlotType
        {
            internal readonly string? Key;
            internal readonly NeoRuntimeTypeKey? Parsed;
            internal readonly string Text;
            internal readonly bool IsReference;

            internal SlotType(string? key, string text, bool isReference)
            {
                Parsed = key is not null && NeoRuntimeTypeKeys.TryParse(key, out NeoRuntimeTypeKey parsed, out _) ? parsed : null;
                Key = Parsed is null ? null : NeoRuntimeTypeKeys.Spell(Parsed);
                Text = text;
                IsReference = isReference;
            }

            internal static SlotType Of(NeoRuntimeTypeKey key)
            {
                string spelled = NeoRuntimeTypeKeys.Spell(key);
                return new SlotType(spelled, spelled, IsReferenceKey(key));
            }
        }

        /// <summary>Thrown when a type names a parameter its environment leaves open.</summary>
        private sealed class OpenTypeException : Exception
        {
        }

        /// <summary>
        /// The closed type <paramref name="binding"/> names in
        /// <paramref name="environment"/> (the server's
        /// <c>genericBindingTypeInfo</c>), or false when a type parameter in
        /// it stays open.
        /// </summary>
        private bool TryDescribeBinding(
            TypeBinding binding,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> environment,
            out SlotType type)
        {
            try
            {
                type = binding.IsOpen
                    ? DescribeParameter(binding.Param!, environment, depth: 0)
                    : Describe(MemberOf(NeoTypeBindingViews.Id(binding)), environment, depth: 0);
                return true;
            }
            catch (OpenTypeException)
            {
                type = default;
                return false;
            }
        }

        /// <summary>
        /// <paramref name="binding"/>'s type in <paramref name="environment"/>
        /// as the server's <c>formatTypeInfo</c> prints it, or null when a
        /// type parameter in it stays open.
        /// </summary>
        internal string? BindingText(TypeBinding binding, IReadOnlyDictionary<string, NeoGenericEnvEntry> environment) =>
            TryDescribeBinding(binding, environment, out SlotType type) ? type.Text : null;

        private Member MemberOf(string? memberId)
        {
            if (memberId is null)
                throw new OpenTypeException();
            if (data?.members is { } members && members.TryGetValue(memberId, out Member? member) && member is not null)
                return member;
            return NeoTypeBindingViews.Member(memberId) ?? throw new OpenTypeException();
        }

        private SlotType DescribeParameter(string parameterId, IReadOnlyDictionary<string, NeoGenericEnvEntry> environment, int depth)
        {
            if (!environment.TryGetValue(parameterId, out NeoGenericEnvEntry? entry) || entry.memberId is not string bound)
                throw new OpenTypeException();
            return Describe(MemberOf(bound), NeoGenericResolution.BindingEnv(environment, entry), depth + 1);
        }

        /// <summary>
        /// The closed type <paramref name="member"/> declares: §5.3's key (a
        /// value type nullable only when optional, a reference type never)
        /// and the text the server prints for it, <c>?</c> on every optional
        /// position.
        /// </summary>
        private SlotType Describe(Member member, IReadOnlyDictionary<string, NeoGenericEnvEntry> environment, int depth)
        {
            if (depth > MaxTypeDepth)
                throw new OpenTypeException();
            bool optional = member.Requirement == NeoMemberRequirementKind.Optional;
            string suffix = optional ? "?" : "";
            // A payload's runtime type can't say whether a reference type is nullable.
            string referenceSuffix = NeoTypeBindingViews.IsPayloadViewId(member.id) ? "" : suffix;
            SlotType Value(string name) => new SlotType(name + suffix, name + suffix, isReference: false);
            SlotType Reference(string? key, string text) => new SlotType(key, text + referenceSuffix, isReference: true);
            SlotType Nested(string id) => Describe(MemberOf(id), environment, depth + 1);
            switch (member)
            {
                case GenericMember generic:
                    return DescribeParameter(generic.genericParamId, environment, depth);
                case IntMember:
                    return Value("int");
                case FloatMember:
                    return Value("float");
                case DecimalMember:
                    return Value("decimal");
                case BoolMember:
                    return Value("bool");
                case Vector2Member:
                    return Value("Vector2");
                case Vector2IntMember:
                    return Value("Vector2Int");
                case Vector3Member:
                    return Value("Vector3");
                case Vector3IntMember:
                    return Value("Vector3Int");
                case ColorMember:
                    return Value("Color");
                case StringMember:
                    return Reference("string", "string");
                case SpriteMember:
                    return Reference("SpriteInfo", "SpriteInfo");
                case AudioMember:
                    return Reference("AudioClipInfo", "AudioClipInfo");
                case DialogueLookupMember { Selection: NeoMemberSelectionKind.Single }:
                    return Reference("NeoDialogueReference", "NeoDialogueReference");
                case DialogueLookupMember:
                    return Reference("Set<NeoDialogueReference>", "Set<NeoDialogueReference>");
                case EnumMember { Selection: NeoMemberSelectionKind.Single } single:
                    return new SlotType("e:" + single.enumId + suffix, EnumName(single.enumId) + suffix, isReference: false);
                case EnumMember multi:
                    return Reference($"Set<e:{multi.enumId}>", $"Set<{EnumName(multi.enumId)}>");
                case TupleMember tuple:
                    {
                        var elements = new SlotType[tuple.elementMemberIds.Length];
                        for (int index = 0; index < elements.Length; index++)
                            elements[index] = Nested(tuple.elementMemberIds[index]);
                        return new SlotType(
                            Join("(", elements.Select(element => element.Key), ")") + suffix,
                            $"({string.Join(", ", elements.Select(element => element.Text))}){suffix}",
                            isReference: false);
                    }
                case SetMember set:
                    {
                        SlotType entry = Nested(set.entryMemberId);
                        return Reference(Join("Set<", new[] { entry.Key }, ">"), $"Set<{entry.Text}>");
                    }
                case ListMember list:
                    {
                        SlotType entry = Nested(list.entryMemberId);
                        return Reference(Join("List<", new[] { entry.Key }, ">"), $"List<{entry.Text}>");
                    }
                case DictionaryMember dictionary:
                    {
                        SlotType entry = Nested(dictionary.entryMemberId);
                        bool enumKey = dictionary.KeyKind == NeoDictionaryKeyKind.Enum;
                        string keyKey = enumKey ? "e:" + dictionary.keyEnumId : "string";
                        string keyText = enumKey ? EnumName(dictionary.keyEnumId!) : "string";
                        return Reference(Join($"Dictionary<{keyKey},", new[] { entry.Key }, ">"), $"Dictionary<{keyText}, {entry.Text}>");
                    }
                case LookupMember lookup:
                    {
                        SlotType target = DescribeTypeInfo(lookup.declaredTypeInfo ?? throw new OpenTypeException(), environment, depth + 1);
                        string? key = Join("Reference<", new[] { target.Key }, ">");
                        string text = $"Reference<{target.Text}>";
                        return lookup.Selection == NeoMemberSelectionKind.Multi
                            ? Reference(Join("Set<", new[] { key }, ">"), $"Set<{text}>")
                            : Reference(key, text);
                    }
                case ObjectMember objectMember:
                    {
                        string? name = ObjectTypeName(objectMember.objectType);
                        return Reference(name, name ?? "object");
                    }
                case ClassMember constructed:
                    {
                        bool isClass = constructed.DeclaredClassId is not null;
                        string id = constructed.DeclaredClassId ?? constructed.DeclaredInterfaceId ?? throw new OpenTypeException();
                        NeoRuntimeTypeKeyKind kind = isClass ? NeoRuntimeTypeKeyKind.Class : NeoRuntimeTypeKeyKind.Interface;
                        List<GenericParamDeclaration> parameters = ParametersOf(kind, id);
                        var keys = new string?[parameters.Count];
                        var texts = new List<string>();
                        if (constructed.TypeArguments is { } bindings)
                        {
                            foreach (KeyValuePair<string, TypeBinding> binding in bindings)
                            {
                                SlotType argument = binding.Value.IsOpen
                                    ? DescribeParameter(binding.Value.Param!, environment, depth)
                                    : Nested(NeoTypeBindingViews.Id(binding.Value));
                                int index = parameters.FindIndex(parameter => parameter.id == binding.Key);
                                if (index >= 0)
                                    keys[index] = argument.Key;
                                texts.Add(argument.Text);
                            }
                        }
                        string name = isClass ? ClassName(id) : InterfaceName(id);
                        string spelled = (isClass ? "c:" : "i:") + id;
                        string? key = keys.Length == 0 ? spelled : Join(spelled + "<", keys, ">");
                        return Reference(key, texts.Count == 0 ? name : $"{name}<{string.Join(", ", texts)}>");
                    }
                case DelegateMember callable:
                    return DescribeCallable(callable.returnTypeInfo, callable.argumentTypes, referenceSuffix, environment, depth);
                case ActionMember action:
                    return DescribeCallable(null, action.argumentTypes, "", environment, depth);
                default:
                    return new SlotType(null, member.kind + referenceSuffix, isReference: true);
            }
        }

        /// <summary>
        /// <c>NeoDelegate&lt;result, arguments...&gt;</c>, or
        /// <c>NeoAction&lt;arguments...&gt;</c> when <paramref name="result"/>
        /// is null: keyed as §5.3 spells it and printed as
        /// <c>formatTypeInfo</c> prints it. An action is never nullable (P62 §2.1).
        /// </summary>
        private SlotType DescribeCallable(
            TypeInfo? result,
            IReadOnlyList<TypeInfo> arguments,
            string suffix,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> environment,
            int depth)
        {
            var keys = new List<string?>();
            var texts = new List<string>();
            if (result is VoidTypeInfo)
            {
                keys.Add("void");
                texts.Add("void");
            }
            else if (result is not null)
            {
                SlotType type = DescribeTypeInfo(result, environment, depth + 1);
                keys.Add(type.Key);
                texts.Add(type.Text);
            }
            foreach (TypeInfo argument in arguments)
            {
                SlotType type = DescribeTypeInfo(argument, environment, depth + 1);
                keys.Add(type.Key);
                texts.Add(type.Text);
            }
            string name = result is null ? NeoAction : NeoDelegate;
            string? key = keys.Count == 0 ? name : Join(name + "<", keys, ">");
            return new SlotType(key, $"{name}<{string.Join(", ", texts)}>{(result is null ? "" : suffix)}", isReference: true);
        }

        /// <summary>A parameter's flat type-info as the typed arm the wire converter reads for its kind.</summary>
        private static TypeInfo Typed(FunctionArgumentTypeInfo argument)
        {
            TypeInfo typed = argument.type switch
            {
                MemberKind.Class => new ClassTypeInfo { classId = argument.classId!, typeArguments = argument.typeArguments },
                MemberKind.Interface => new InterfaceTypeInfo { interfaceId = argument.interfaceId!, typeArguments = argument.typeArguments },
                MemberKind.Enum => new EnumTypeInfo { enumId = argument.enumId! },
                MemberKind.List or MemberKind.Set or MemberKind.Dictionary => new CollectionTypeInfo
                {
                    entryTypeInfo = argument.entryTypeInfo!,
                    keyEnumId = argument.keyEnumId,
                },
                MemberKind.Lookup => new LookupTypeInfo { entryTypeInfo = argument.entryTypeInfo! },
                // A function's own type parameter is never closed by a stored slot.
                MemberKind.Generic => new GenericTypeInfo { genericParamId = argument.genericParamId ?? throw new OpenTypeException() },
                MemberKind.NSDelegate => new DelegateTypeInfo { returnTypeInfo = argument.returnTypeInfo!, argumentTypes = argument.argumentTypes! },
                MemberKind.NSAction => new ActionTypeInfo { argumentTypes = argument.argumentTypes! },
                MemberKind.Reference => new ReferenceTypeInfo { targetTypeInfo = argument.targetTypeInfo! },
                MemberKind.Tuple => new TupleTypeInfo { elements = argument.elements! },
                MemberKind.Object => new ObjectTypeInfo { objectType = argument.objectType },
                _ => new PrimitiveTypeInfo(),
            };
            typed.type = argument.type;
            typed.required = argument.required;
            return typed;
        }

        /// <summary>A declared type-info (a Lookup's target) as <see cref="Describe"/> reads a member.</summary>
        private SlotType DescribeTypeInfo(TypeInfo typeInfo, IReadOnlyDictionary<string, NeoGenericEnvEntry> environment, int depth)
        {
            if (depth > MaxTypeDepth)
                throw new OpenTypeException();
            if (typeInfo is FunctionArgumentTypeInfo argument)
                typeInfo = Typed(argument);
            string suffix = typeInfo.required ? "" : "?";
            SlotType Value(string name) => new SlotType(name + suffix, name + suffix, isReference: false);
            SlotType Reference(string? key, string text) => new SlotType(key, text + suffix, isReference: true);
            SlotType Nested(TypeInfo nested) => DescribeTypeInfo(nested, environment, depth + 1);
            switch (typeInfo)
            {
                case GenericTypeInfo generic:
                    return DescribeParameter(generic.genericParamId, environment, depth);
                case EnumTypeInfo enumType:
                    return new SlotType("e:" + enumType.enumId + suffix, EnumName(enumType.enumId) + suffix, isReference: false);
                case ObjectTypeInfo objectType:
                    {
                        string? name = ObjectTypeName(objectType.objectType);
                        return Reference(name, name ?? "object");
                    }
                case TupleTypeInfo tuple:
                    {
                        SlotType[] elements = tuple.elements.Select(element => Nested(element.typeInfo)).ToArray();
                        return new SlotType(
                            Join("(", elements.Select(element => element.Key), ")") + suffix,
                            $"({string.Join(", ", elements.Select(element => element.Text))}){suffix}",
                            isReference: false);
                    }
                case CollectionTypeInfo collection:
                    {
                        SlotType entry = Nested(collection.entryTypeInfo);
                        switch (collection.type)
                        {
                            case MemberKind.List:
                                return Reference(Join("List<", new[] { entry.Key }, ">"), $"List<{entry.Text}>");
                            case MemberKind.Set:
                                return Reference(Join("Set<", new[] { entry.Key }, ">"), $"Set<{entry.Text}>");
                            default:
                                {
                                    string keyKey = collection.keyEnumId is string keyEnumId ? "e:" + keyEnumId : "string";
                                    string keyText = collection.keyEnumId is string enumId ? EnumName(enumId) : "string";
                                    return Reference(Join($"Dictionary<{keyKey},", new[] { entry.Key }, ">"), $"Dictionary<{keyText}, {entry.Text}>");
                                }
                        }
                    }
                case LookupTypeInfo lookup:
                    {
                        SlotType entry = Nested(lookup.entryTypeInfo);
                        return Reference(Join("Set<", new[] { entry.Key }, ">"), $"Set<{entry.Text}>");
                    }
                case ReferenceTypeInfo reference:
                    {
                        SlotType target = Nested(reference.targetTypeInfo);
                        return Reference(Join("Reference<", new[] { target.Key }, ">"), $"Reference<{target.Text}>");
                    }
                case ClassTypeInfo classType:
                    return DescribeConstructed(NeoRuntimeTypeKeyKind.Class, classType.classId, classType.typeArguments, suffix, environment, depth);
                case InterfaceTypeInfo interfaceType:
                    return DescribeConstructed(NeoRuntimeTypeKeyKind.Interface, interfaceType.interfaceId, interfaceType.typeArguments, suffix, environment, depth);
                case DelegateTypeInfo callable:
                    return DescribeCallable(callable.returnTypeInfo, callable.argumentTypes, suffix, environment, depth);
                case ActionTypeInfo action:
                    return DescribeCallable(null, action.argumentTypes, "", environment, depth);
                default:
                    switch (typeInfo.type)
                    {
                        case MemberKind.Int:
                            return Value("int");
                        case MemberKind.Float:
                            return Value("float");
                        case MemberKind.Decimal:
                            return Value("decimal");
                        case MemberKind.Bool:
                            return Value("bool");
                        case MemberKind.Vector2:
                            return Value("Vector2");
                        case MemberKind.Vector2Int:
                            return Value("Vector2Int");
                        case MemberKind.Vector3:
                            return Value("Vector3");
                        case MemberKind.Vector3Int:
                            return Value("Vector3Int");
                        case MemberKind.Color:
                            return Value("Color");
                        case MemberKind.String:
                            return Reference("string", "string");
                        case MemberKind.Sprite:
                            return Reference("SpriteInfo", "SpriteInfo");
                        case MemberKind.Audio:
                            return Reference("AudioClipInfo", "AudioClipInfo");
                        case MemberKind.DialogueLookup:
                            return Reference("NeoDialogueReference", "NeoDialogueReference");
                        default:
                            return Reference(null, typeInfo.type.ToString());
                    }
            }
        }

        private SlotType DescribeConstructed(
            NeoRuntimeTypeKeyKind kind,
            string id,
            Dictionary<string, TypeInfo>? typeArguments,
            string suffix,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> environment,
            int depth)
        {
            List<GenericParamDeclaration> parameters = ParametersOf(kind, id);
            var keys = new string?[parameters.Count];
            var texts = new List<string>();
            if (typeArguments is not null)
            {
                foreach (KeyValuePair<string, TypeInfo> argument in typeArguments)
                {
                    SlotType type = DescribeTypeInfo(argument.Value, environment, depth + 1);
                    int index = parameters.FindIndex(parameter => parameter.id == argument.Key);
                    if (index >= 0)
                        keys[index] = type.Key;
                    texts.Add(type.Text);
                }
            }
            bool isClass = kind == NeoRuntimeTypeKeyKind.Class;
            string name = isClass ? ClassName(id) : InterfaceName(id);
            string spelled = (isClass ? "c:" : "i:") + id;
            string? key = keys.Length == 0 ? spelled : Join(spelled + "<", keys, ">");
            return new SlotType(key, (texts.Count == 0 ? name : $"{name}<{string.Join(", ", texts)}>") + suffix, isReference: true);
        }

        /// <summary><paramref name="open"/>, the parts joined by commas, <paramref name="close"/>; null when a part has no key.</summary>
        private static string? Join(string open, IEnumerable<string?> parts, string close)
        {
            var list = new List<string>();
            foreach (string? part in parts)
            {
                if (part is null)
                    return null;
                list.Add(part);
            }
            return open + string.Join(",", list) + close;
        }

        private static string? ObjectTypeName(NeoObjectTypeKind? objectType) => objectType switch
        {
            null => ObjectKey,
            NeoObjectTypeKind.ValueType => "ValueType",
            NeoObjectTypeKind.Enum => "Enum",
            NeoObjectTypeKind.Type => "Type",
            NeoObjectTypeKind.CultureInfo => "CultureInfo",
            NeoObjectTypeKind.NumberFormatInfo => "NumberFormatInfo",
            _ => null,
        };

        private List<GenericParamDeclaration> ParametersOf(NeoRuntimeTypeKeyKind kind, string id)
        {
            List<GenericParamDeclaration>? parameters = kind == NeoRuntimeTypeKeyKind.Class
                ? data?.classes is { } classes && classes.TryGetValue(id, out NeoSchemaClass? schemaClass) ? schemaClass.genericParams : null
                : data?.interfaces is { } interfaces && interfaces.TryGetValue(id, out Interface? record) ? record.genericParams : null;
            return parameters ?? NoParameters;
        }

        private static readonly List<GenericParamDeclaration> NoParameters = new List<GenericParamDeclaration>();

        private string ClassName(string id) =>
            data?.classes is { } classes && classes.TryGetValue(id, out NeoSchemaClass? schemaClass) ? schemaClass.name : id;

        private string InterfaceName(string id) =>
            data?.interfaces is { } interfaces && interfaces.TryGetValue(id, out Interface? record) ? record.name : id;

        private string EnumName(string id) =>
            data?.enums is { } enums && enums.TryGetValue(id, out Json.Enum? record) ? record.name : id;

        /// <summary>"a IComparable" reads "an IComparable".</summary>
        private static string WithArticle(string text) =>
            text.Length > 0 && "AEIOUaeiou".IndexOf(text[0]) >= 0 ? $"an {text}" : $"a {text}";

        /// <summary>
        /// <see cref="NeoGeneratedTypesSupport.ClosedSlotRequired(TypeInfo, bool)"/>
        /// for a slot member closed over a binding member.
        /// </summary>
        internal static NeoMemberRequirementKind ClosedRequirement(NeoMemberRequirementKind declared, Member bound) =>
            bound.Requirement == NeoMemberRequirementKind.Required
                && (declared == NeoMemberRequirementKind.Required || IsValueType(bound))
                ? NeoMemberRequirementKind.Required
                : NeoMemberRequirementKind.Optional;

        /// <summary>The value kinds of a closed type info; its enums are single-select.</summary>
        internal static bool IsValueKind(MemberKind kind) => kind is MemberKind.Int
            or MemberKind.Float
            or MemberKind.Decimal
            or MemberKind.Bool
            or MemberKind.Enum
            or MemberKind.Tuple
            or MemberKind.Vector2
            or MemberKind.Vector2Int
            or MemberKind.Vector3
            or MemberKind.Vector3Int
            or MemberKind.Color;

        /// <summary>The value types (§2's table): primitives, vectors, <c>Color</c>, enums, tuples.</summary>
        private static bool IsValueType(Member member) => member switch
        {
            IntMember or FloatMember or DecimalMember or BoolMember => true,
            Vector2Member or Vector2IntMember or Vector3Member or Vector3IntMember or ColorMember => true,
            TupleMember => true,
            EnumMember enumMember => enumMember.Selection == NeoMemberSelectionKind.Single,
            _ => false,
        };

        private NeoBoxForm UncachedBox(string keyText, TypeBinding valueType)
        {
            string? violation = NeoRuntimeTypeKeys.StoredViolation(keyText, out NeoRuntimeTypeKey? key);
            if (violation is null)
                violation = NeoRuntimeTypeKeys.BoxKeyViolation(keyText, key!);
            if (violation is not null)
                return new NeoBoxForm(NeoBoxFormKind.Invalid, invalid: violation);
            string? unstorable = UnstorableType(key!);
            if (unstorable is not null)
                return new NeoBoxForm(NeoBoxFormKind.Invalid, invalid: $"valueType '{NeoTypeNames.Of(valueType, data)}' names {unstorable}, which isn't storable");
            if (key!.Kind == NeoRuntimeTypeKeyKind.Class)
                return new NeoBoxForm(NeoBoxFormKind.Comparer);
            if (key.Kind == NeoRuntimeTypeKeyKind.Builtin && key.Name == ObjectKey)
                return new NeoBoxForm(NeoBoxFormKind.Identity);
            Member? member = NeoTypeBindingViews.PayloadView(valueType, out string invalid);
            return member is not null
                ? new NeoBoxForm(NeoBoxFormKind.Member, member)
                : new NeoBoxForm(NeoBoxFormKind.Invalid, invalid: invalid);
        }

        /// <summary>
        /// The closed binding a runtime type key spells, binding class and
        /// interface arguments to this export's parameter ids in declaration
        /// order; null for a key with no stored form here.
        /// </summary>
        internal TypeBinding? BindingOfKey(NeoRuntimeTypeKey key)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Nullable:
                    return BindingOfKey(key.Arguments![0])?.WithRequired(false);
                case NeoRuntimeTypeKeyKind.Enum:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Enum, true, enumId: key.Id));
                case NeoRuntimeTypeKeyKind.Tuple:
                    return PositionalBinding(new TypeHead(MemberKind.Tuple, true), key.Arguments);
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                    {
                        bool isClass = key.Kind == NeoRuntimeTypeKeyKind.Class;
                        List<GenericParamDeclaration> parameters = ParametersOf(key.Kind, key.Id!);
                        NeoRuntimeTypeKey[] arguments = key.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
                        if (parameters.Count != arguments.Length)
                            return null;
                        Dictionary<string, TypeBinding>? bindings = arguments.Length == 0
                            ? null
                            : new Dictionary<string, TypeBinding>(arguments.Length, StringComparer.Ordinal);
                        for (int index = 0; index < arguments.Length; index++)
                        {
                            if (BindingOfKey(arguments[index]) is not TypeBinding argument)
                                return null;
                            bindings![parameters[index].id] = argument;
                        }
                        TypeHead head = isClass
                            ? new TypeHead(MemberKind.Class, true, classId: key.Id)
                            : new TypeHead(MemberKind.Interface, true, interfaceId: key.Id);
                        return TypeBinding.Closed(head, bindings);
                    }
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    {
                        MemberKind? kind = key.Name switch
                        {
                            "List" => MemberKind.List,
                            "Set" => MemberKind.Set,
                            "Dictionary" => MemberKind.Dictionary,
                            "Reference" => MemberKind.Reference,
                            NeoDelegate => MemberKind.NSDelegate,
                            NeoAction => MemberKind.NSAction,
                            _ => null,
                        };
                        return kind is MemberKind builtIn ? PositionalBinding(new TypeHead(builtIn, true), key.Arguments) : null;
                    }
                case NeoRuntimeTypeKeyKind.Builtin:
                    {
                        TypeHead? head = key.Name switch
                        {
                            "object" => new TypeHead(MemberKind.Object, true),
                            "ValueType" => new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.ValueType),
                            "Enum" => new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.Enum),
                            "Type" => new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.Type),
                            "CultureInfo" => new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.CultureInfo),
                            "NumberFormatInfo" => new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo),
                            NeoAction => new TypeHead(MemberKind.NSAction, true),
                            "string" => new TypeHead(MemberKind.String, true),
                            "SpriteInfo" => new TypeHead(MemberKind.Sprite, true),
                            "AudioClipInfo" => new TypeHead(MemberKind.Audio, true),
                            "NeoDialogueReference" => new TypeHead(MemberKind.DialogueLookup, true),
                            "int" => new TypeHead(MemberKind.Int, true),
                            "float" => new TypeHead(MemberKind.Float, true),
                            "decimal" => new TypeHead(MemberKind.Decimal, true),
                            "bool" => new TypeHead(MemberKind.Bool, true),
                            "Vector2" => new TypeHead(MemberKind.Vector2, true),
                            "Vector2Int" => new TypeHead(MemberKind.Vector2Int, true),
                            "Vector3" => new TypeHead(MemberKind.Vector3, true),
                            "Vector3Int" => new TypeHead(MemberKind.Vector3Int, true),
                            "Color" => new TypeHead(MemberKind.Color, true),
                            _ => null,
                        };
                        return head is null ? null : TypeBinding.Closed(head);
                    }
                default:
                    return null;
            }
        }

        private TypeBinding? PositionalBinding(TypeHead head, NeoRuntimeTypeKey[]? arguments)
        {
            if (arguments is null || arguments.Length == 0)
                return TypeBinding.Closed(head);
            var bindings = new Dictionary<string, TypeBinding>(arguments.Length, StringComparer.Ordinal);
            for (int index = 0; index < arguments.Length; index++)
            {
                if (BindingOfKey(arguments[index]) is not TypeBinding argument)
                    return null;
                bindings[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = argument;
            }
            return TypeBinding.Closed(head, bindings);
        }

        /// <summary>The §1.2 non-storable type <paramref name="key"/> names at any depth, or null.</summary>
        private string? UnstorableType(NeoRuntimeTypeKey key)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Enum:
                    return key.Id == NeoPrimitiveConformance.NumberStylesEnumId ? $"'{NumberStyles}'" : null;
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                case NeoRuntimeTypeKeyKind.Class:
                case NeoRuntimeTypeKeyKind.Interface:
                case NeoRuntimeTypeKeyKind.Tuple:
                    if (key.Arguments is null)
                        return null;
                    foreach (NeoRuntimeTypeKey argument in key.Arguments)
                    {
                        if (UnstorableType(argument) is string found)
                            return found;
                    }
                    return null;
                default:
                    return null;
            }
        }

    }
}
