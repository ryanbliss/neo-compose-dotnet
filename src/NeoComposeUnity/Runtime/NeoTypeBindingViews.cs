// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P127 §12.1. The member a stored type reads as. Declarations store type
    /// arguments as <see cref="TypeBinding"/>s, while generic substitution,
    /// value building and validation read members, so each distinct binding
    /// reads as one in-memory member view, interned for the life of the
    /// process. A view's id is process-local and never stored. Twin of the
    /// TypeScript <c>type-binding-views.ts</c>.
    /// </summary>
    internal static class NeoTypeBindingViews
    {
        internal const string Prefix = "binding-view:";

        /// <summary>The id of <see cref="ComparerSlotMember"/>.</summary>
        internal const string ComparerSlotMemberId = "comparer:slot";

        /// <summary>
        /// The member an authored comparer's owned row stores under (§9.5):
        /// an <c>object</c> slot, which owns the class row it holds.
        /// </summary>
        internal static readonly ObjectMember ComparerSlotMember = NewComparerSlotMember();

        /// <summary>Which strings a view's family holds, and whether its references admit null.</summary>
        private enum Family
        {
            /// <summary>A declared slot's type: strings are localized, as a declared <c>string</c> is.</summary>
            Declared,
            /// <summary>A box's payload: strings are plain runtime values and reference types admit null.</summary>
            Payload,
        }

        private readonly struct ViewKey : IEquatable<ViewKey>
        {
            private readonly TypeBinding binding;
            private readonly Family family;
            private readonly int position;

            internal ViewKey(TypeBinding binding, Family family, int position)
            {
                this.binding = binding;
                this.family = family;
                this.position = position;
            }

            public bool Equals(ViewKey other) => family == other.family && position == other.position && binding.Equals(other.binding);

            public override bool Equals(object? obj) => obj is ViewKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(binding, family, position);
        }

        private readonly struct Interned
        {
            internal readonly Member? Member;
            internal readonly string? Invalid;

            internal Interned(Member? member, string? invalid)
            {
                Member = member;
                Invalid = invalid;
            }
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<ViewKey, Interned> ByKey = new Dictionary<ViewKey, Interned>();
        private static readonly Dictionary<string, (Member Member, TypeBinding Binding, Family Family)> ById =
            new Dictionary<string, (Member Member, TypeBinding Binding, Family Family)>(StringComparer.Ordinal);
        private static int nextId;

        internal static bool IsViewId(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The view or comparer slot <paramref name="id"/> names, or null.</summary>
        internal static Member? Member(string id)
        {
            if (id == ComparerSlotMemberId)
                return ComparerSlotMember;
            if (!IsViewId(id))
                return null;
            lock (Gate)
                return ById.TryGetValue(id, out (Member Member, TypeBinding Binding, Family Family) view) ? view.Member : null;
        }

        /// <summary>The binding a view id stands for, or null.</summary>
        internal static TypeBinding? BindingOf(string id)
        {
            if (!IsViewId(id))
                return null;
            lock (Gate)
                return ById.TryGetValue(id, out (Member Member, TypeBinding Binding, Family Family) view) ? view.Binding : null;
        }

        /// <summary>Whether <paramref name="id"/> names a payload view (<see cref="PayloadView"/>).</summary>
        internal static bool IsPayloadViewId(string id)
        {
            if (!IsViewId(id))
                return false;
            lock (Gate)
                return ById.TryGetValue(id, out (Member Member, TypeBinding Binding, Family Family) view) && view.Family == Family.Payload;
        }

        /// <summary>
        /// The member a type argument reads as: a closed type, whose own
        /// arguments may forward parameters, or a forwarded parameter, which
        /// reads as a Generic member. Throws for a binding no member
        /// represents (void, an opaque type, a definition).
        /// </summary>
        internal static Member View(TypeBinding argument) => Get(argument, Family.Declared);

        /// <summary><see cref="View"/>, or null with <paramref name="invalid"/> when no member represents the binding.</summary>
        internal static Member? TryView(TypeBinding argument, out string invalid)
        {
            lock (Gate)
            {
                Interned interned = Intern(new ViewKey(argument, Family.Declared, -1), argument, Family.Declared, null);
                invalid = interned.Invalid ?? "";
                return interned.Member;
            }
        }

        /// <summary>The id of <see cref="View"/>.</summary>
        internal static string Id(TypeBinding argument) => View(argument).id;

        /// <summary>
        /// The member a box's payload stores as (§1.3), for a closed
        /// runtime-identity binding: strings are plain, a reference type admits
        /// null because runtime types erase reference nullability, and stored
        /// delegates validate by shape. Null with <paramref name="invalid"/>
        /// when the binding has no stored form.
        /// </summary>
        internal static Member? PayloadView(TypeBinding binding, out string invalid)
        {
            lock (Gate)
            {
                Interned interned = Intern(new ViewKey(binding, Family.Payload, -1), binding, Family.Payload, null);
                invalid = interned.Invalid ?? "";
                return interned.Member;
            }
        }

        private static Member Get(TypeBinding binding, Family family)
        {
            lock (Gate)
            {
                Interned interned = Intern(new ViewKey(binding, family, -1), binding, family, null);
                return interned.Member ?? throw new InvalidOperationException(interned.Invalid);
            }
        }

        /// <summary>Interns the view at <paramref name="key"/>, building it on first use; a tuple position copies its element's fields.</summary>
        private static Interned Intern(ViewKey key, TypeBinding binding, Family family, Member? fields)
        {
            if (ByKey.TryGetValue(key, out Interned cached))
                return cached;
            Interned interned;
            try
            {
                Member member = fields is null ? Fields(binding, family) : Copy(fields);
                member.id = Prefix + nextId++;
                member.projectId = "";
                member.name = NeoTypeNames.Of(binding);
                member.DeclaredRequirement = RequirementOf(binding, family);
                ById[member.id] = (member, binding, family);
                interned = new Interned(member, null);
            }
            catch (InvalidBindingException exception)
            {
                interned = new Interned(null, exception.Message);
            }
            ByKey[key] = interned;
            return interned;
        }

        private static Member Nested(TypeBinding binding, Family family)
        {
            Interned interned = Intern(new ViewKey(binding, family, -1), binding, family, null);
            return interned.Member ?? throw new InvalidBindingException(interned.Invalid!);
        }

        /// <summary>
        /// A value type is required unless its head is nullable. A declared
        /// view keeps a reference head's annotation; a payload's reference
        /// type admits null (§12.1).
        /// </summary>
        private static NeoMemberRequirementKind RequirementOf(TypeBinding binding, Family family)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return NeoMemberRequirementKind.Required;
            TypeHead head = binding.Head!;
            bool required = family == Family.Payload && !TypeBindings.IsValueType(head) ? false : head.Required;
            return required ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional;
        }

        private static Member Fields(TypeBinding binding, Family family)
        {
            switch (binding.Kind)
            {
                case TypeBindingKind.Open:
                    if (family == Family.Payload)
                        throw new InvalidBindingException($"'{NeoTypeNames.Of(binding)}' is open; only a closed type has a stored form");
                    return new GenericMember { kind = MemberKind.Generic, genericParamId = binding.Param! };
                case TypeBindingKind.Definition:
                case TypeBindingKind.MethodParam:
                    throw new InvalidBindingException($"'{NeoTypeNames.Of(binding)}' is a {binding.Kind} binding, which no member represents");
            }
            TypeHead head = binding.Head!;
            if (head.IsOpaque)
                throw new InvalidBindingException($"'{head.OpaqueName}' is opaque; only a Type payload names it");
            switch (head.Type)
            {
                case MemberKind.Void:
                    throw new InvalidBindingException("'void' has no stored form");
                case MemberKind.Class:
                    return new ClassMember { kind = MemberKind.Class, DeclaredClassId = head.ClassId, classArguments = Arguments(binding) };
                case MemberKind.Interface:
                    return new ClassMember { kind = MemberKind.Class, DeclaredInterfaceId = head.InterfaceId, interfaceArguments = Arguments(binding) };
                case MemberKind.Enum:
                    return new EnumMember { kind = MemberKind.Enum, enumId = head.EnumId! };
                case MemberKind.Object:
                    return new ObjectMember { kind = MemberKind.Object, objectType = head.ObjectType };
                case MemberKind.String:
                    return family == Family.Payload
                        ? new StringMember { kind = MemberKind.String, Format = NeoStringFormatKind.Plain }
                        : new StringMember { kind = MemberKind.String };
                case MemberKind.Int:
                    return new IntMember { kind = MemberKind.Int };
                case MemberKind.Float:
                    return new FloatMember { kind = MemberKind.Float };
                case MemberKind.Decimal:
                    return new DecimalMember { kind = MemberKind.Decimal };
                case MemberKind.Bool:
                    return new BoolMember { kind = MemberKind.Bool };
                case MemberKind.Vector2:
                    return new Vector2Member { kind = MemberKind.Vector2 };
                case MemberKind.Vector2Int:
                    return new Vector2IntMember { kind = MemberKind.Vector2Int };
                case MemberKind.Vector3:
                    return new Vector3Member { kind = MemberKind.Vector3 };
                case MemberKind.Vector3Int:
                    return new Vector3IntMember { kind = MemberKind.Vector3Int };
                case MemberKind.Color:
                    return new ColorMember { kind = MemberKind.Color };
                case MemberKind.Sprite:
                    return new SpriteMember { kind = MemberKind.Sprite };
                case MemberKind.Audio:
                    return new AudioMember { kind = MemberKind.Audio };
                case MemberKind.DialogueLookup:
                    return new DialogueLookupMember { kind = MemberKind.DialogueLookup };
                case MemberKind.List:
                    return new ListMember { kind = MemberKind.List, entryMemberId = Nested(binding.At(0), family).id };
                case MemberKind.Set:
                    return SetFields(binding, family);
                case MemberKind.Dictionary:
                    {
                        TypeBinding key = binding.At(0);
                        var member = new DictionaryMember { kind = MemberKind.Dictionary, entryMemberId = Nested(binding.At(1), family).id };
                        if (key.Kind == TypeBindingKind.Closed && key.Head!.Type == MemberKind.Enum)
                        {
                            member.KeyKind = NeoDictionaryKeyKind.Enum;
                            member.keyEnumId = key.Head.EnumId;
                        }
                        else if (key.Kind != TypeBindingKind.Closed || key.Head!.Type != MemberKind.String)
                        {
                            throw new InvalidBindingException(
                                $"Dictionary keys must be string or an enum type, not '{NeoTypeNames.Of(key)}'");
                        }
                        return member;
                    }
                case MemberKind.Reference:
                    return new LookupMember { kind = MemberKind.Lookup, declaredTypeInfo = ReferenceTarget(binding) };
                case MemberKind.Tuple:
                    {
                        // A tuple's element ids must be distinct, so each
                        // position reads as its own view: `(int, int)` has two.
                        // A binding names no elements, so each keeps its ItemN name.
                        var ids = new string[binding.Bindings!.Count];
                        for (int index = 0; index < ids.Length; index++)
                        {
                            TypeBinding element = binding.At(index);
                            Member elementFields = Nested(element, family);
                            Member position = Intern(new ViewKey(binding, family, index), element, family, elementFields).Member!;
                            position.name = $"Item{index + 1}";
                            ids[index] = position.id;
                        }
                        return new TupleMember { kind = MemberKind.Tuple, elementMemberIds = ids };
                    }
                case MemberKind.NSDelegate:
                    {
                        if (family == Family.Payload)
                        {
                            // Stored delegate values validate by shape, not by signature.
                            return new DelegateMember
                            {
                                kind = MemberKind.NSDelegate,
                                returnTypeInfo = new UnknownTypeInfo { type = MemberKind.Unknown, required = false },
                                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                            };
                        }
                        var signature = (DelegateTypeInfo)NeoTypeBindings.ArgumentTypeInfo(binding);
                        return new DelegateMember
                        {
                            kind = MemberKind.NSDelegate,
                            returnTypeInfo = signature.returnTypeInfo,
                            argumentTypes = Parameters(signature.argumentTypes),
                        };
                    }
                case MemberKind.NSAction:
                    {
                        // A listener set validates by shape, as a delegate does.
                        TypeInfo[] arguments = family == Family.Payload || binding.Bindings is null
                            ? Array.Empty<TypeInfo>()
                            : ((ActionTypeInfo)NeoTypeBindings.ArgumentTypeInfo(binding)).argumentTypes;
                        return new ActionMember { kind = MemberKind.NSAction, argumentTypes = Parameters(arguments) };
                    }
                default:
                    throw new InvalidBindingException($"'{NeoTypeNames.Of(binding)}' has no stored form");
            }
        }

        private static Dictionary<string, TypeBinding>? Arguments(TypeBinding binding)
        {
            if (binding.Bindings is null)
                return null;
            var arguments = new Dictionary<string, TypeBinding>(binding.Bindings.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeBinding> pair in binding.Bindings)
            {
                if (pair.Value.Kind is TypeBindingKind.Definition or TypeBindingKind.MethodParam)
                    throw new InvalidBindingException($"Type argument '{pair.Key}' is a {pair.Value.Kind} binding");
                arguments[pair.Key] = pair.Value;
            }
            return arguments;
        }

        /// <summary>A view delegate's parameters, named <c>p1…</c> as authored delegates are.</summary>
        private static FunctionArgumentTypeInfo[] Parameters(TypeInfo[] types)
        {
            var parameters = new FunctionArgumentTypeInfo[types.Length];
            for (int index = 0; index < types.Length; index++)
                parameters[index] = NeoFunctionTypeArguments.AsArgument(types[index], $"p{index + 1}");
            return parameters;
        }

        /// <summary>P112 §1.2: a <c>Reference&lt;T&gt;</c> names a value type, never code.</summary>
        private static TypeInfo ReferenceTarget(TypeBinding reference)
        {
            TypeBinding target = reference.At(0);
            if (target.Kind == TypeBindingKind.Closed && target.Head!.Type is MemberKind.NSDelegate or MemberKind.NSAction)
            {
                throw new InvalidBindingException(
                    $"'{NeoTypeNames.Of(reference)}' references '{NeoTypeNames.Of(target)}', which is not a value type");
            }
            return NeoTypeBindings.ArgumentTypeInfo(target);
        }

        /// <summary>
        /// P108's lowering: a Set of enum options is a multi-select Enum, of
        /// <c>Reference&lt;T&gt;</c> a multi-select Lookup, and of
        /// <c>NeoDialogueReference</c> a multi-select DialogueLookup; any
        /// other Set holds entry rows its entry rules admit, whose strings are
        /// plain as a declared <c>Set&lt;string&gt;</c>'s are.
        /// </summary>
        private static Member SetFields(TypeBinding set, Family family)
        {
            TypeBinding element = set.At(0);
            if (element.Kind == TypeBindingKind.Closed)
            {
                TypeHead head = element.Head!;
                switch (head.Type)
                {
                    case MemberKind.Enum:
                        return new EnumMember { kind = MemberKind.Enum, enumId = head.EnumId!, Selection = NeoMemberSelectionKind.Multi };
                    case MemberKind.DialogueLookup:
                        return new DialogueLookupMember { kind = MemberKind.DialogueLookup, Selection = NeoMemberSelectionKind.Multi };
                    case MemberKind.Reference:
                        return new LookupMember
                        {
                            kind = MemberKind.Lookup,
                            declaredTypeInfo = ReferenceTarget(element),
                            Selection = NeoMemberSelectionKind.Multi,
                        };
                }
            }
            Member entry = family == Family.Payload || element.Kind == TypeBindingKind.Closed
                ? Nested(element, Family.Payload)
                : Nested(element, family);
            // Keys erase nullable references, so only a spelled `?` on a value type is nullable.
            bool nullable = element.Kind == TypeBindingKind.Closed && !element.Head!.Required && TypeBindings.IsValueType(element.Head);
            string? error = SetEntryError(entry.kind, nullable);
            if (error is not null)
                throw new InvalidBindingException($"'{NeoTypeNames.Of(set)}': {error}");
            return new SetMember { kind = MemberKind.Set, entryMemberId = entry.id };
        }

        /// <summary>P108 §1.3's rule for a Set entry, worded as the TypeScript <c>setEntryMemberError</c>.</summary>
        private static string? SetEntryError(MemberKind kind, bool nullable)
        {
            switch (kind)
            {
                case MemberKind.List:
                case MemberKind.Dictionary:
                case MemberKind.Set:
                    return "`Set` elements can't be collections.";
                case MemberKind.Lookup:
                    return "Spell the type `Set<Reference<T>>`; it lowers to a multi-select Lookup.";
                case MemberKind.DialogueLookup:
                    return "A `Set<NeoDialogueReference>` is a multi-select DialogueLookup, not a Set member.";
                case MemberKind.NSDelegate:
                    return "`Set` elements can't be delegates.";
                case MemberKind.NSAction:
                    return "`Set` elements can't be actions.";
                case MemberKind.Object:
                case MemberKind.Generic:
                    return null;
            }
            return nullable ? "`Set` elements can't be nullable." : null;
        }

        /// <summary>A tuple position's own view: its element's fields under a distinct id.</summary>
        private static Member Copy(Member fields)
        {
            return fields.ShallowClone();
        }

        private static ObjectMember NewComparerSlotMember()
        {
            var member = new ObjectMember
            {
                id = ComparerSlotMemberId,
                projectId = "",
                name = "comparer",
                kind = MemberKind.Object,
            };
            member.DeclaredRequirement = NeoMemberRequirementKind.Required;
            return member;
        }

        private sealed class InvalidBindingException : Exception
        {
            internal InvalidBindingException(string message)
                : base(message)
            {
            }
        }
    }
}
