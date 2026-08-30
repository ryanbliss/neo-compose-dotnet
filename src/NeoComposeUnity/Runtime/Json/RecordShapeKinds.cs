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
            // canonical writers canonicalize an empty optional axis to omission, but
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

    internal sealed class NeoResolvedMemberShape
    {
        private readonly object?[] chainResolvedValues =
            new object?[MemberChainResolvedFields.ResolutionFields.Count];
        private uint chainResolvedPresence;

        internal NeoMemberRequirementKind Requirement;
        internal NeoMemberMutabilityKind Mutability;
        internal NeoMemberModifierKind Modifier;
        internal NeoMemberAccessKind Access;
        internal NeoMemberStorage Storage;
        internal NeoStringFormatKind Format;
        internal NeoMemberSearchByKind SearchBy;
        internal NeoDictionaryKeyKind DictionaryKeyKind;
        internal NeoListKind ListKind;
        internal NeoMemberPayloadKind Payload;
        internal NeoMemberSelectionKind Selection;
        internal NeoFunctionDispatchKind Dispatch;
        internal NeoFunctionBodyKind BodyMode;

        internal void CopyChainResolvedValuesFrom(NeoResolvedMemberShape inherited)
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

        internal NeoResolvedMemberShape Clone()
        {
            var clone = new NeoResolvedMemberShape
            {
                Requirement = Requirement,
                Mutability = Mutability,
                Modifier = Modifier,
                Access = Access,
                Storage = Storage,
                Format = Format,
                SearchBy = SearchBy,
                DictionaryKeyKind = DictionaryKeyKind,
                ListKind = ListKind,
                Payload = Payload,
                Selection = Selection,
                Dispatch = Dispatch,
                BodyMode = BodyMode,
            };
            clone.CopyChainResolvedValuesFrom(this);
            return clone;
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
            NeoResolvedMemberShape inherited,
            NeoResolvedMemberShape resolved)
        {
            resolved.CopyChainResolvedValuesFrom(inherited);
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
                    resolved.ClearChainResolvedValue(index);
                }
                else
                {
                    resolved.SetChainResolvedValue(index, value);
                }
            }

            // TS resolveMember couples authored clears to their compiled IR.
            if (IsDeclaredNull(member, "code"))
            {
                resolved.ClearChainResolvedValue(IndexOf("getter"));
                if (!member.DeclaresWireField("action"))
                {
                    resolved.ClearChainResolvedValue(IndexOf("action"));
                }
            }
            if (IsDeclaredNull(member, "setterCode"))
            {
                resolved.ClearChainResolvedValue(IndexOf("setter"));
            }
            if (IsDeclaredNull(member, "uiAction")
                && !member.DeclaresWireField("action"))
            {
                resolved.ClearChainResolvedValue(IndexOf("action"));
            }
        }

        internal static void Materialize(Member member, NeoResolvedMemberShape resolved)
        {
            for (int index = 0; index < ResolutionFields.Count; index++)
            {
                resolved.TryGetChainResolvedValue(index, out object? value);
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
            // Restore each sparse wire row before rebuilding the resolved
            // projection. Runtime consumers historically read these DTO
            // fields directly, so the final pass materializes only the
            // centralized chain-resolved fields while retaining their
            // original has-own metadata for validation and serialization.
            foreach (Member member in members.Values)
            {
                member.PrepareChainResolvedFields();
                member.resolvedShape = null;
            }

            foreach (Member member in members.Values)
            {
                Resolve(member, members);
            }
        }

        private static NeoResolvedMemberShape Resolve(
            Member member,
            IReadOnlyDictionary<string, Member> members)
        {
            if (member.resolvedShape is not null) return member.resolvedShape;

            // Walk iteratively so a corrupt export cannot turn a long override
            // chain into a CLR stack overflow. Cached ancestors keep total work
            // linear in the member count for a valid graph.
            var chain = new List<Member>();
            var chainIds = new HashSet<string>();
            NeoResolvedMemberShape inherited = Defaults();
            Member? current = member;
            while (current is not null && current.resolvedShape is null)
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
            if (current?.resolvedShape is not null)
            {
                inherited = current.resolvedShape;
            }

            for (int index = chain.Count - 1; index >= 0; index--)
            {
                Member resolved = chain[index];
                resolved.resolvedShape = Apply(resolved, inherited);
                MemberChainResolvedFields.Materialize(resolved, resolved.resolvedShape);
                inherited = resolved.resolvedShape;
            }
            return member.resolvedShape ?? Defaults();
        }

        private static NeoResolvedMemberShape Apply(
            Member member,
            NeoResolvedMemberShape inherited)
        {
            bool genericReset = member is GenericMember;
            var resolved = new NeoResolvedMemberShape
            {
                Requirement = member.DeclaredRequirement
                    ?? (genericReset
                        ? NeoMemberRequirementKind.Optional
                        : inherited.Requirement),
                // Access and mutability are declaration-local projections in
                // the TS resolver. Unlike the other sparse override axes,
                // absence resets them to their canonical default.
                Mutability = member.DeclaredMutability ?? NeoMemberMutabilityKind.Mutable,
                Modifier = member.DeclaredModifier ?? inherited.Modifier,
                Access = member.DeclaredAccess ?? NeoMemberAccessKind.Public,
                // Storage is nullable on the DTO so absent can inherit while
                // an explicit ordinal zero stops the override-member chain.
                Storage = member.DeclaredStorage ?? inherited.Storage,
                Format = (member as StringMember)?.DeclaredFormat ?? inherited.Format,
                SearchBy = (member as StringMember)?.DeclaredSearchBy ?? inherited.SearchBy,
                DictionaryKeyKind = (member as DictionaryMember)?.DeclaredKeyKind
                    ?? inherited.DictionaryKeyKind,
                ListKind = (member as ListMember)?.DeclaredListKind ?? inherited.ListKind,
                Payload = member switch
                {
                    ClassMember classMember => classMember.DeclaredPayload ?? inherited.Payload,
                    GenericMember genericMember => genericMember.DeclaredPayload ?? inherited.Payload,
                    _ => inherited.Payload,
                },
                Selection = member switch
                {
                    EnumMember enumMember => enumMember.DeclaredSelection ?? inherited.Selection,
                    LookupMember lookupMember => lookupMember.DeclaredSelection ?? inherited.Selection,
                    DialogueLookupMember dialogueMember => dialogueMember.DeclaredSelection ?? inherited.Selection,
                    _ => inherited.Selection,
                },
                Dispatch = member switch
                {
                    FunctionMember function => function.DeclaredDispatch ?? inherited.Dispatch,
                    NSFunctionMember function => function.DeclaredDispatch ?? inherited.Dispatch,
                    _ => inherited.Dispatch,
                },
                BodyMode = (member as NSFunctionMember)?.DeclaredBodyMode ?? inherited.BodyMode,
            };
            MemberChainResolvedFields.Apply(member, inherited, resolved);
            if (genericReset && !member.DeclaresWireField("defaultValue"))
            {
                resolved.ClearChainResolvedValue(
                    MemberChainResolvedFields.IndexOf("defaultValue"));
            }
            return resolved;
        }

        private static NeoResolvedMemberShape Defaults() => new NeoResolvedMemberShape
        {
            Requirement = NeoMemberRequirementKind.Optional,
            Mutability = NeoMemberMutabilityKind.Mutable,
            Modifier = NeoMemberModifierKind.Virtual,
            Access = NeoMemberAccessKind.Public,
            Storage = NeoMemberStorage.Inherit,
            Format = NeoStringFormatKind.Localized,
            SearchBy = NeoMemberSearchByKind.None,
            DictionaryKeyKind = NeoDictionaryKeyKind.String,
            ListKind = NeoListKind.Ordered,
            Payload = NeoMemberPayloadKind.Full,
            Selection = NeoMemberSelectionKind.Single,
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            BodyMode = NeoFunctionBodyKind.Code,
        };
    }
}
