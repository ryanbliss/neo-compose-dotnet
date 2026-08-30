// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    // Persisted ordinals are append-only. Zero is the wire default and may be
    // omitted by exporters.
    public enum NeoClassModifierKind { Open = 0, Abstract = 1, Sealed = 2 }
    public enum NeoClassVisibilityKind { Visible = 0, Hidden = 1 }
    public enum NeoMemberModifierKind { Virtual = 0, Sealed = 1, Abstract = 2, Static = 3 }
    public enum NeoMemberAccessKind { Public = 0, Protected = 1, Private = 2 }
    public enum NeoStringFormatKind { Localized = 0, Plain = 1 }
    public enum NeoMemberSearchByKind { None = 0, MemberKey = 1 }
    public enum NeoDictionaryKeyKind { String = 0, Enum = 1 }
    public enum NeoListKind { Ordered = 0, Unordered = 1 }
    public enum NeoInterfaceMemberKind { Property = 0, Function = 1 }
    public enum NeoGenericParamConstraintKind { Class = 0, Enum = 1 }
    public enum NeoGenericBindingKind { Generic = 0, Member = 1 }
    public enum NeoMemberRequirementKind { Optional = 0, Required = 1 }
    public enum NeoMemberMutabilityKind { Mutable = 0, ReadOnly = 1 }
    public enum NeoMemberSelectionKind { Single = 0, Multi = 1 }
    public enum NeoFunctionDispatchKind { Synchronous = 0, Asynchronous = 1 }
    public enum NeoFunctionBodyKind { Code = 0, UI = 1 }
    public enum NeoPropertyAccessorsKind { Get = 0, GetSet = 1 }
    public enum NeoMemberPayloadKind { Full = 0, Partial = 1 }
    public enum NeoListIndexKind { Bucket = 0, Unique = 1 }
    public enum NeoColumnVisibilityKind { Visible = 0, Hidden = 1 }
    public enum NeoColumnPinKind { None = 0, Leading = 1 }
    public enum NeoColumnOverflowKind { Clip = 0, Wrap = 1 }

    internal static class StrictRecordShapeEnums
    {
        internal static T ReadRequired<T>(JObject obj, string field, string context)
            where T : struct
        {
            JToken? token = obj[field];
            if (token is null)
            {
                throw new JsonSerializationException(
                    $"{context} is missing '{field}'.");
            }
            return Read<T>(token, field, context);
        }

        internal static T ReadDefaulted<T>(
            JObject obj,
            string field,
            string context,
            T defaultValue)
            where T : struct
        {
            JToken? token = obj[field];
            if (token is null) return defaultValue;
            if (token.Type == JTokenType.Null)
            {
                obj.Property(field)?.Remove();
                return defaultValue;
            }
            return Read<T>(token, field, context);
        }

        internal static void ValidateOptional<T>(JObject obj, string field, string context)
            where T : struct
        {
            JToken? token = obj[field];
            if (token is null) return;
            // P80 writers canonicalize an empty optional axis to omission, but
            // readers still fold historical/storage-boundary nulls into that
            // same default state (§7). Optional enum tokens preserve the
            // distinction long enough for validation; removing the token then
            // keeps both nullable and value-type DTO fields canonical.
            if (token.Type == JTokenType.Null)
            {
                obj.Property(field)?.Remove();
                return;
            }
            Read<T>(token, field, context);
        }

        private static T Read<T>(JToken token, string field, string context)
            where T : struct
        {
            if (token.Type != JTokenType.Integer)
            {
                throw new JsonSerializationException(
                    $"{context} field '{field}' must be a numeric enum ordinal.");
            }
            long ordinal = token.Value<long>();
            if (ordinal < int.MinValue || ordinal > int.MaxValue
                || !typeof(T).IsEnum
                || !System.Enum.IsDefined(typeof(T), (int)ordinal))
            {
                throw new JsonSerializationException(
                    $"{context} field '{field}' has unknown ordinal '{ordinal}'.");
            }
            return (T)System.Enum.ToObject(typeof(T), (int)ordinal);
        }
    }

    internal sealed class NeoEffectiveMemberShape
    {
        private readonly object?[] chainResolvedValues =
            new object?[MemberChainResolvedFields.ResolutionFields.Count];
        private uint chainResolvedPresence;

        internal NeoMemberRequirementKind requirement;
        internal NeoMemberMutabilityKind mutability;
        internal NeoMemberModifierKind modifier;
        internal NeoMemberAccessKind access;
        internal NeoMemberStorage storage;
        internal NeoStringFormatKind format;
        internal NeoMemberSearchByKind searchBy;
        internal NeoDictionaryKeyKind dictionaryKeyKind;
        internal NeoListKind listKind;
        internal NeoMemberPayloadKind payload;
        internal NeoMemberSelectionKind selection;
        internal NeoFunctionDispatchKind dispatch;
        internal NeoFunctionBodyKind bodyMode;

        internal void CopyChainResolvedValuesFrom(NeoEffectiveMemberShape inherited)
        {
            Array.Copy(
                inherited.chainResolvedValues,
                chainResolvedValues,
                chainResolvedValues.Length);
            chainResolvedPresence = inherited.chainResolvedPresence;
        }

        internal void SetChainResolvedValue(int index, object value)
        {
            chainResolvedValues[index] = value;
            chainResolvedPresence |= 1u << index;
        }

        internal void ClearChainResolvedValue(int index)
        {
            chainResolvedValues[index] = null;
            chainResolvedPresence &= ~(1u << index);
        }

        internal bool TryGetChainResolvedValue(int index, out object? value)
        {
            if ((chainResolvedPresence & (1u << index)) == 0)
            {
                value = null;
                return false;
            }
            value = chainResolvedValues[index];
            return true;
        }

        internal T? GetChainResolvedValue<T>(string field)
            where T : class
        {
            int index = MemberChainResolvedFields.IndexOf(field);
            return index >= 0
                && TryGetChainResolvedValue(index, out object? value)
                    ? value as T
                    : null;
        }

        internal void SetChainResolvedValue(string field, object? value)
        {
            int index = MemberChainResolvedFields.IndexOf(field);
            if (index < 0) return;
            if (value is null)
            {
                ClearChainResolvedValue(index);
            }
            else
            {
                SetChainResolvedValue(index, value);
            }
        }
    }

    /// <summary>
    /// Central projection for the TS-side
    /// <c>CHAIN_RESOLVED_OPTIONAL_MEMBER_FIELDS</c> contract. The first 18
    /// entries intentionally mirror that list exactly. The final three are
    /// server-compiled companions whose inheritance is coupled to authored
    /// code/body clears by <c>resolveMember</c>.
    /// </summary>
    internal static class MemberChainResolvedFields
    {
        internal static readonly IReadOnlyList<string> CanonicalFields = new[]
        {
            "defaultValue",
            "storageKey",
            "minValue",
            "maxValue",
            "decimalPoints",
            "indexes",
            "columnSettings",
            "schemaKeyOrder",
            "classArguments",
            "collectionValueId",
            "declaredTypeInfo",
            "targetTypeInfo",
            "valueTypeInfo",
            "dialogueGroupId",
            "code",
            "setterCode",
            "templateId",
            "uiAction",
        };

        internal static readonly IReadOnlyList<string> ResolutionFields = new[]
        {
            "defaultValue",
            "storageKey",
            "minValue",
            "maxValue",
            "decimalPoints",
            "indexes",
            "columnSettings",
            "schemaKeyOrder",
            "classArguments",
            "collectionValueId",
            "declaredTypeInfo",
            "targetTypeInfo",
            "valueTypeInfo",
            "dialogueGroupId",
            "code",
            "setterCode",
            "templateId",
            "uiAction",
            "getter",
            "setter",
            "action",
        };

        private static readonly Dictionary<string, int> indexes = BuildIndexes();
        private static readonly Dictionary<(Type, string), MemberInfo?> accessors = new();
        private static readonly object accessorLock = new();

        internal static int IndexOf(string field) =>
            indexes.TryGetValue(field, out int index) ? index : -1;

        internal static void Apply(
            Member member,
            NeoEffectiveMemberShape inherited,
            NeoEffectiveMemberShape effective)
        {
            effective.CopyChainResolvedValuesFrom(inherited);
            for (int index = 0; index < ResolutionFields.Count; index++)
            {
                string field = ResolutionFields[index];
                if (!member.DeclaresWireField(field)
                    || !member.TryReadOriginalChainResolvedField(field, out object? value))
                {
                    continue;
                }
                if (value is null)
                {
                    effective.ClearChainResolvedValue(index);
                }
                else
                {
                    effective.SetChainResolvedValue(index, value);
                }
            }

            // TS resolveMember couples authored clears to their compiled IR.
            if (IsDeclaredNull(member, "code"))
            {
                effective.ClearChainResolvedValue(IndexOf("getter"));
                if (!member.DeclaresWireField("action"))
                {
                    effective.ClearChainResolvedValue(IndexOf("action"));
                }
            }
            if (IsDeclaredNull(member, "setterCode"))
            {
                effective.ClearChainResolvedValue(IndexOf("setter"));
            }
            if (IsDeclaredNull(member, "uiAction")
                && !member.DeclaresWireField("action"))
            {
                effective.ClearChainResolvedValue(IndexOf("action"));
            }
        }

        internal static void Materialize(Member member, NeoEffectiveMemberShape effective)
        {
            for (int index = 0; index < ResolutionFields.Count; index++)
            {
                effective.TryGetChainResolvedValue(index, out object? value);
                string field = ResolutionFields[index];
                if (TryWrite(member, field, value))
                {
                    member.RecordMaterializedChainResolvedField(field, value);
                }
            }
        }

        internal static bool TryRead(Member member, string field, out object? value)
        {
            MemberInfo? accessor = ResolveAccessor(member.GetType(), field);
            switch (accessor)
            {
                case FieldInfo fieldInfo:
                    value = fieldInfo.GetValue(member);
                    return true;
                case PropertyInfo propertyInfo when propertyInfo.CanRead:
                    value = propertyInfo.GetValue(member);
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        internal static bool TryWrite(Member member, string field, object? value)
        {
            MemberInfo? accessor = ResolveAccessor(member.GetType(), field);
            switch (accessor)
            {
                case FieldInfo fieldInfo:
                    fieldInfo.SetValue(member, value);
                    return true;
                case PropertyInfo propertyInfo when propertyInfo.CanWrite:
                    propertyInfo.SetValue(member, value);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDeclaredNull(Member member, string field) =>
            member.DeclaresWireField(field)
            && member.TryReadOriginalChainResolvedField(field, out object? value)
            && value is null;

        private static MemberInfo? ResolveAccessor(Type type, string field)
        {
            var key = (type, field);
            lock (accessorLock)
            {
                if (accessors.TryGetValue(key, out MemberInfo? cached)) return cached;
                MemberInfo? accessor = type.GetField(
                        field,
                        BindingFlags.Instance | BindingFlags.Public)
                    ?? (MemberInfo?)type.GetProperty(
                        field,
                        BindingFlags.Instance | BindingFlags.Public);
                accessors[key] = accessor;
                return accessor;
            }
        }

        private static Dictionary<string, int> BuildIndexes()
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < ResolutionFields.Count; index++)
            {
                result.Add(ResolutionFields[index], index);
            }
            return result;
        }
    }

    internal static class NeoMemberShapeResolution
    {
        internal static void ResolveAll(IReadOnlyDictionary<string, Member> members)
        {
            // Restore each sparse wire row before rebuilding the effective
            // projection. Runtime consumers historically read these DTO
            // fields directly, so the final pass materializes only the
            // centralized chain-resolved fields while retaining their
            // original has-own metadata for validation and serialization.
            foreach (Member member in members.Values)
            {
                member.PrepareChainResolvedFields();
                member.effectiveShape = null;
            }

            foreach (Member member in members.Values)
            {
                Resolve(member, members);
            }
        }

        private static NeoEffectiveMemberShape Resolve(
            Member member,
            IReadOnlyDictionary<string, Member> members)
        {
            if (member.effectiveShape is not null) return member.effectiveShape;

            // Walk iteratively so a corrupt export cannot turn a long override
            // chain into a CLR stack overflow. Cached ancestors keep total work
            // linear in the member count for a valid graph.
            var chain = new List<Member>();
            var chainIds = new HashSet<string>();
            NeoEffectiveMemberShape inherited = Defaults();
            Member? current = member;
            while (current is not null && current.effectiveShape is null)
            {
                // Inheritance validation reports the cycle separately. Defaults
                // keep this pre-validation projection bounded.
                if (!chainIds.Add(current.id)) break;
                chain.Add(current);
                if (string.IsNullOrEmpty(current.extendsMemberId)
                    || !members.TryGetValue(current.extendsMemberId!, out Member parent))
                {
                    current = null;
                    break;
                }
                current = parent;
            }
            if (current?.effectiveShape is not null)
            {
                inherited = current.effectiveShape;
            }

            for (int index = chain.Count - 1; index >= 0; index--)
            {
                Member resolved = chain[index];
                resolved.effectiveShape = Apply(resolved, inherited);
                MemberChainResolvedFields.Materialize(resolved, resolved.effectiveShape);
                inherited = resolved.effectiveShape;
            }
            return member.effectiveShape ?? Defaults();
        }

        private static NeoEffectiveMemberShape Apply(
            Member member,
            NeoEffectiveMemberShape inherited)
        {
            bool genericReset = member is GenericMember;
            var effective = new NeoEffectiveMemberShape
            {
                requirement = member.requirement
                    ?? (genericReset
                        ? NeoMemberRequirementKind.Optional
                        : inherited.requirement),
                // Access and mutability are declaration-local projections in
                // the TS resolver. Unlike the other sparse override axes,
                // absence resets them to their canonical default.
                mutability = member.mutability ?? NeoMemberMutabilityKind.Mutable,
                modifier = member.modifier ?? inherited.modifier,
                access = member.access ?? NeoMemberAccessKind.Public,
                // Storage is nullable on the DTO so absent can inherit while
                // an explicit ordinal zero stops the override-member chain.
                storage = member.storage ?? inherited.storage,
                format = (member as StringMember)?.format ?? inherited.format,
                searchBy = (member as StringMember)?.searchBy ?? inherited.searchBy,
                dictionaryKeyKind = (member as DictionaryMember)?.keyKind
                    ?? inherited.dictionaryKeyKind,
                listKind = (member as ListMember)?.listKind ?? inherited.listKind,
                payload = member switch
                {
                    ClassMember classMember => classMember.payload ?? inherited.payload,
                    GenericMember genericMember => genericMember.payload ?? inherited.payload,
                    _ => inherited.payload,
                },
                selection = member switch
                {
                    EnumMember enumMember => enumMember.selection ?? inherited.selection,
                    LookupMember lookupMember => lookupMember.selection ?? inherited.selection,
                    DialogueLookupMember dialogueMember => dialogueMember.selection ?? inherited.selection,
                    _ => inherited.selection,
                },
                dispatch = member switch
                {
                    FunctionMember function => function.dispatch ?? inherited.dispatch,
                    NSFunctionMember function => function.dispatch ?? inherited.dispatch,
                    _ => inherited.dispatch,
                },
                bodyMode = (member as NSFunctionMember)?.bodyMode ?? inherited.bodyMode,
            };
            MemberChainResolvedFields.Apply(member, inherited, effective);
            if (genericReset && !member.DeclaresWireField("defaultValue"))
            {
                effective.ClearChainResolvedValue(
                    MemberChainResolvedFields.IndexOf("defaultValue"));
            }
            return effective;
        }

        private static NeoEffectiveMemberShape Defaults() => new NeoEffectiveMemberShape
        {
            requirement = NeoMemberRequirementKind.Optional,
            mutability = NeoMemberMutabilityKind.Mutable,
            modifier = NeoMemberModifierKind.Virtual,
            access = NeoMemberAccessKind.Public,
            storage = NeoMemberStorage.Inherit,
            format = NeoStringFormatKind.Localized,
            searchBy = NeoMemberSearchByKind.None,
            dictionaryKeyKind = NeoDictionaryKeyKind.String,
            listKind = NeoListKind.Ordered,
            payload = NeoMemberPayloadKind.Full,
            selection = NeoMemberSelectionKind.Single,
            dispatch = NeoFunctionDispatchKind.Synchronous,
            bodyMode = NeoFunctionBodyKind.Code,
        };
    }
}
