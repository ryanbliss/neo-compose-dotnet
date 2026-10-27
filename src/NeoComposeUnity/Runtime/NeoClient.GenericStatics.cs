// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        /// <summary>
        /// The Lookup rows the authored ownership walk meets while a project
        /// with generic statics is checked; null otherwise.
        /// </summary>
        private List<(string rowId, LookupMember member)>? genericStaticLookupRows;

        /// <summary>
        /// Every row a generic static's initializer owns, by the static's
        /// label; empty when the project declares none. Built once: the
        /// initializers are authored rows, and a construction's state copies
        /// them under new ids.
        /// </summary>
        private Dictionary<string, string>? genericStaticOwnedRows;

        /// <summary>P127 §5.3's refusal, worded as the server's <c>genericStaticBindingError</c>.</summary>
        internal static string GenericStaticBindingError(string label) =>
            $"Static '{label}' has state per closed construction, so only code can reference it.";

        /// <summary>
        /// P127 §5.3. Why a stored binding can't target
        /// <paramref name="targetId"/>, a generic static's initializer or a row
        /// it owns; null when it can. The server's
        /// <c>genericStaticTargetViolation</c>, word for word.
        /// </summary>
        internal string? GenericStaticTargetViolation(string targetId)
        {
            genericStaticOwnedRows ??= GenericStaticIndex.Of(data) is { } statics
                ? GenericStaticOwnedRows(statics)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            return genericStaticOwnedRows.TryGetValue(targetId, out string? label)
                ? $"targets value \"{targetId}\". {GenericStaticBindingError(label)}"
                : null;
        }

        /// <summary>
        /// P127 §5.3 for a host write of a Lookup's selection into
        /// <paramref name="slot"/>: no stored binding names a generic static's
        /// row. The slot's label, <paramref name="ownerClassName"/>.<paramref name="key"/>
        /// or a bare <paramref name="key"/>, is built only for a refusal.
        /// </summary>
        internal void RequireNoGenericStaticTargets(Member slot, string? ownerClassName, string key, NeoValueWritePayload? payload)
        {
            if (slot is not LookupMember)
                return;
            object? value = payload?.value is NeoValuePayload envelope ? envelope.value : payload?.value;
            if (value is not string[] targets)
                return;
            foreach (string? target in targets)
            {
                if (target is not null && GenericStaticTargetViolation(target) is { } violation)
                {
                    string label = ownerClassName is null ? key : $"{ownerClassName}.{key}";
                    throw new InvalidOperationException($"Lookup '{label}' {violation}");
                }
            }
        }

        /// <summary>
        /// P127 §5.3. A generic static's <c>valueId</c> is its initializer, so a
        /// stored binding naming the static, its value or a row nested in it
        /// would read no construction's state. Refuses every record-level
        /// binding the server refuses: a Lookup's collection, a class's
        /// <c>@settings(target:)</c>, variant folders, dialogue records and
        /// Reference targets. Runs once the authored ownership walk has
        /// collected the Lookup rows.
        /// </summary>
        private void ValidateGenericStaticBindings(GenericStaticIndex statics)
        {
            Dictionary<string, string> owned = genericStaticOwnedRows = GenericStaticOwnedRows(statics);
            string? OwnedLabel(string valueId) => owned.TryGetValue(valueId, out string? label) ? label : null;
            void Refuse(string error) =>
                throw new InvalidOperationException($"NeoClient: {error} {NeoProjectExportContract.RegenerateAction}");
            void BindValue(string recordLabel, string key, string? valueId)
            {
                if (valueId is not null && OwnedLabel(valueId) is string label)
                    Refuse($"{recordLabel} binds \"{key}\" to \"{valueId}\". {GenericStaticBindingError(label)}");
            }

            foreach (Member member in data.members.Values)
            {
                if (member is not LookupMember lookup)
                    continue;
                string memberLabel = $"Member \"{member.name}\" ({member.id})";
                BindValue(memberLabel, "collectionValueId", lookup.collectionValueId);
                foreach (string target in lookup.defaultValue?.value ?? Array.Empty<string>())
                    BindValue(memberLabel, "defaultValue", target);
                if (lookup.collectionMemberId is string collectionMemberId && statics.Label(collectionMemberId) is string label)
                    Refuse($"{memberLabel} binds collection \"{collectionMemberId}\". {GenericStaticBindingError(label)}");
            }
            foreach (VariantFolderRecord folder in data.variantFolders?.Values ?? Enumerable.Empty<VariantFolderRecord>())
            {
                if (folder.binding is null)
                    continue;
                string folderLabel = $"Variant folder \"{folder.id}\"";
                BindValue(folderLabel, "collectionValueId", folder.binding.collectionValueId);
                if (statics.Label(folder.binding.collectionMemberId) is string label)
                    Refuse($"{folderLabel} binds collection \"{folder.binding.collectionMemberId}\". {GenericStaticBindingError(label)}");
            }
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                if (schemaClass.targetMemberId is string targetMemberId && statics.Label(targetMemberId) is string label)
                    Refuse($"Class \"{schemaClass.name}\" ({schemaClass.id}) targets member \"{targetMemberId}\". {GenericStaticBindingError(label)}");
            }
            void BindDialogueRecord(object record, string recordLabel)
            {
                if (DialogueGenericStaticError(JToken.FromObject(record), recordLabel, statics, OwnedLabel) is string error)
                    Refuse(error);
            }
            foreach (Dialogue dialogue in data.dialogues.Values)
                BindDialogueRecord(dialogue, DescribeDialogueRecord("Dialogue", dialogue.id, dialogue.name));
            foreach (DialogueGroup group in data.dialogueGroups.Values)
                BindDialogueRecord(group, DescribeDialogueRecord("Dialogue group", group.id, group.name));
            foreach ((string rowId, LookupMember lookup) in genericStaticLookupRows ?? Enumerable.Empty<(string, LookupMember)>())
            {
                if (!data.values.TryGetValue(rowId, out MemberValue row) || row is not ArrayMemberValue { value: { } targets })
                    continue;
                foreach (string target in targets)
                {
                    if (GenericStaticTargetViolation(target) is string violation)
                        Refuse($"Lookup \"{lookup.name}\" value \"{rowId}\" {violation}");
                }
            }
        }

        /// <summary>
        /// Every row a generic static's initializer owns at any depth, the
        /// initializer included, by the static's label: one walk over the
        /// same owned child links the authored ownership walk follows.
        /// </summary>
        private Dictionary<string, string> GenericStaticOwnedRows(GenericStaticIndex statics)
        {
            var owned = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new Stack<(string valueId, Member? member, string label)>();
            foreach ((string valueId, string memberId) in statics.ValueIds)
            {
                data.members.TryGetValue(memberId, out Member? member);
                pending.Push((valueId, member, statics.Label(memberId)!));
            }
            while (pending.Count > 0)
            {
                (string valueId, Member? member, string label) = pending.Pop();
                if (!owned.TryAdd(valueId, label) || !data.values.TryGetValue(valueId, out MemberValue row))
                    continue;
                foreach (var child in EnumerateOwnedChildLinks(row, member))
                    pending.Push((child.valueId, child.member, label));
                if (member is ListMember list && IsUnorderedList(list)
                    && authoredEntriesByContainer.TryGetValue(valueId, out var entries))
                {
                    Member? entryMember = TryResolveCollectionEntryMember(list, row);
                    foreach (string entryId in entries)
                        pending.Push((entryId, entryMember, label));
                }
            }
            return owned;
        }

        /// <summary>The server's <c>describeDialogueRecord</c>.</summary>
        private static string DescribeDialogueRecord(string kindLabel, string? id, string? name) =>
            name is not null && id is not null ? $"{kindLabel} \"{name}\" ({id})"
            : id is not null ? $"{kindLabel} \"{id}\""
            : kindLabel;

        private static readonly HashSet<string> DialogueMemberIdKeys = new(StringComparer.Ordinal)
        {
            "memberId",
            "collectionMemberId",
        };

        private static readonly HashSet<string> DialogueValueIdKeys = new(StringComparer.Ordinal)
        {
            "valueId",
            "collectionValueId",
            "lookupValueId",
            "primaryLinkedValueId",
        };

        /// <summary>
        /// A dialogue binds members through <c>memberId</c> /
        /// <c>collectionMemberId</c> (UI logic, text variables, lookup groups)
        /// and values through linked values and lookup ids, compiled bodies
        /// included. NeoFlow code is not a binding: it reaches a construction
        /// as <c>Box&lt;int&gt;.Held</c>, so a reusable function's code object
        /// is skipped whole, as the server's <c>isNeoFlowCode</c> skips it.
        /// </summary>
        private static string? DialogueGenericStaticError(
            JToken record,
            string recordLabel,
            GenericStaticIndex statics,
            Func<string, string?> ownedLabel)
        {
            var pending = new Stack<JToken>();
            pending.Push(record);
            while (pending.Count > 0)
            {
                JToken token = pending.Pop();
                if (token is JArray array)
                {
                    for (int index = array.Count - 1; index >= 0; index--)
                        pending.Push(array[index]);
                    continue;
                }
                if (token is not JObject obj || IsNeoFlowCode(obj))
                    continue;
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value.Type != JTokenType.String)
                    {
                        pending.Push(property.Value);
                        continue;
                    }
                    string id = property.Value.Value<string>()!;
                    string? label = DialogueMemberIdKeys.Contains(property.Name) ? statics.Label(id)
                        : DialogueValueIdKeys.Contains(property.Name) ? ownedLabel(id)
                        : null;
                    if (label is not null)
                        return $"{recordLabel} binds \"{property.Name}\" to \"{id}\". {GenericStaticBindingError(label)}";
                }
            }
            return null;
        }

        /// <summary>Code logic and reusable text-variable functions; inline paths are bindings.</summary>
        private static bool IsNeoFlowCode(JObject value)
        {
            if (value["code"]?.Type != JTokenType.String || value["sourceFunctionId"]?.Type != JTokenType.String)
                return false;
            return value["sourceInline"] is not JValue { Type: JTokenType.Boolean } inline || !inline.Value<bool>();
        }

        /// <summary>
        /// P127 §5.3. Statics of generic classes and interfaces, whose state
        /// exists per closed construction, labeled <c>Box&lt;T&gt;.Held</c>.
        /// </summary>
        private sealed class GenericStaticIndex
        {
            private readonly Dictionary<string, string> labels = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> memberIdsByValueId = new(StringComparer.Ordinal);

            /// <summary>Each initializer row and its static's member id.</summary>
            internal IEnumerable<KeyValuePair<string, string>> ValueIds => memberIdsByValueId;

            /// <summary>The static's label when <paramref name="memberId"/> is a generic static.</summary>
            internal string? Label(string memberId) => labels.TryGetValue(memberId, out string? label) ? label : null;

            /// <summary>The project's generic statics, or null when it declares none.</summary>
            internal static GenericStaticIndex? Of(ProjectData data)
            {
                GenericStaticIndex? index = null;
                foreach (NeoSchemaClass owner in data.classes.Values)
                {
                    if (owner.genericParams is not { Count: > 0 } parameters)
                        continue;
                    foreach (KeyValuePair<string, string> field in owner.schema)
                    {
                        if (field.Value is null
                            || !data.members.TryGetValue(field.Value, out Member? member)
                            || member.Modifier != NeoMemberModifierKind.Static)
                        {
                            continue;
                        }
                        (index ??= new GenericStaticIndex()).Add(field.Value, member, $"{ConstructionLabel(owner.name, parameters)}.{field.Key}");
                    }
                }
                foreach (Interface owner in data.interfaces.Values)
                {
                    if (owner.genericParams is not { Count: > 0 } parameters)
                        continue;
                    foreach (KeyValuePair<string, InterfaceMember> declared in owner.members)
                    {
                        if (declared.Value.implementationMemberId is not string memberId || !declared.Value.IsStatic)
                            continue;
                        data.members.TryGetValue(memberId, out Member? member);
                        (index ??= new GenericStaticIndex()).Add(memberId, member, $"{ConstructionLabel(owner.name, parameters)}.{declared.Key}");
                    }
                }
                return index;
            }

            private void Add(string memberId, Member? member, string label)
            {
                labels[memberId] = label;
                if (member?.valueId is string valueId)
                    memberIdsByValueId[valueId] = memberId;
            }

            private static string ConstructionLabel(string name, List<GenericParamDeclaration> parameters) =>
                $"{name}<{string.Join(", ", parameters.Select(parameter => parameter.name))}>";
        }
    }
}
