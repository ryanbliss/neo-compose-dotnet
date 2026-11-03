// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PropertyInfo = System.Reflection.PropertyInfo;
using NeoCompose.Runtime.NeoScript.Numerics;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Globalization;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static readonly ConditionalWeakTable<IFormatProvider, ClrFormatProvider> clrFormatProviders = new();

        internal sealed class ClrFormatProvider : INeoAuthoredFormatProvider
        {
            internal readonly IFormatProvider Source;
            internal ClrFormatProvider(IFormatProvider source) => Source = source;
            public object? GetNumberFormat() => Source.GetFormat(typeof(System.Globalization.NumberFormatInfo));
        }

        internal static object? ImportFormatProvider(object? value) => value is IFormatProvider provider
            && value is not (System.Globalization.CultureInfo or System.Globalization.NumberFormatInfo or INeoValueReference)
                ? clrFormatProviders.GetValue(provider, source => new ClrFormatProvider(source)) : value;

        internal static bool IsClrFormatProvider(object? value, TypeInfo type) =>
            value is System.Globalization.CultureInfo or System.Globalization.NumberFormatInfo or ClrFormatProvider
            && type.type == MemberKind.Interface
            && (type is InterfaceTypeInfo contract ? contract.interfaceId
                : (type as FunctionArgumentTypeInfo)?.interfaceId) == NeoPrimitiveConformance.InterfaceId("IFormatProvider");

        private static object? CallClrFormatProvider(ClrFormatProvider provider, object? formatType, Context ctx)
        {
            Type? clrType = formatType is NeoTypeValue type ? NeoGeneratedTypesSupport.ClrTypeOf(ctx.client, type.Binding) : null;
            object? result = provider.Source.GetFormat(clrType!);
            return result is null ? null : NeoGeneratedTypesSupport.BoxClrArgument(ctx.client, NeoValueOwnership.Session, result, ctx, "IFormatProvider.GetFormat return");
        }

        private static readonly Dictionary<string, PropertyInfo> numberFormatProperties =
            NeoStorablePayloads.NumberFormatKeys.Select(NeoStorablePayloads.NumberFormatProperty)
                .ToDictionary(property => property.Name, StringComparer.Ordinal);

        private static readonly HashSet<string> cultureMembers = new(StringComparer.Ordinal)
        {
            ".ctor", "InvariantCulture", "CurrentCulture", "CurrentUICulture", "GetCultureInfo", "ReadOnly",
            "Name", "DisplayName", "EnglishName", "NativeName", "TwoLetterISOLanguageName", "ThreeLetterISOLanguageName",
            "ThreeLetterWindowsLanguageName", "LCID", "Parent", "IsNeutralCulture", "IsReadOnly", "NumberFormat", "GetFormat", "Clone",
        };

        internal static bool IsGlobalizationMember(string? owner, string? member) => member is not null && (owner switch
        {
            "CultureInfo" => cultureMembers.Contains(member),
            "NumberFormatInfo" => member is ".ctor" or "InvariantInfo" or "CurrentInfo" or "GetInstance" or "ReadOnly" or "IsReadOnly" or "GetFormat" or "Clone"
                || numberFormatProperties.ContainsKey(member),
            _ => false,
        });

        private static object? EvalGlobalizationMember(GlobalizationMemberPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? receiver = pointer.receiver is null ? null : EvalPointer(pointer.receiver, scope, ctx);
            if (pointer.receiver is not null && receiver is null)
            {
                if (pointer.optional == true)
                    return null;
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            }
            var args = new object?[pointer.args.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = EvalPointer(pointer.args[i], scope, ctx);
            if (pointer.owner == "CultureInfo")
            {
                switch (pointer.member)
                {
                    case ".ctor":
                        return NeoCultures.New((string?)args[0]);
                    case "InvariantCulture":
                        return NeoCultures.InvariantCulture;
                    case "CurrentCulture":
                        return NeoCultures.CurrentCulture;
                    case "CurrentUICulture":
                        return NeoCultures.CurrentUICulture;
                    case "GetCultureInfo":
                        return NeoCultures.GetCultureInfo((string?)args[0]);
                    case "ReadOnly":
                        return NeoCultures.ReadOnly((CultureInfo?)args[0]);
                }
                if (receiver is not CultureInfo culture)
                    throw new NeoScriptPreExecutionValidationError("A CultureInfo member requires a culture receiver.");
                return pointer.member switch
                {
                    "Name" => culture.Name,
                    "DisplayName" => culture.DisplayName,
                    "EnglishName" => culture.EnglishName,
                    "NativeName" => culture.NativeName,
                    "TwoLetterISOLanguageName" => culture.TwoLetterISOLanguageName,
                    "ThreeLetterISOLanguageName" => culture.ThreeLetterISOLanguageName,
                    "ThreeLetterWindowsLanguageName" => culture.ThreeLetterWindowsLanguageName,
                    "LCID" => culture.LCID,
                    "Parent" => NeoCultures.Parent(culture),
                    "IsNeutralCulture" => culture.IsNeutralCulture,
                    "IsReadOnly" => culture.IsReadOnly,
                    "NumberFormat" => culture.NumberFormat,
                    "Clone" => NeoCultures.Clone(culture),
                    "GetFormat" => IsNumberFormatType(args[0]) ? culture.NumberFormat : null,
                    _ => throw new NeoScriptPreExecutionValidationError($"Unknown CultureInfo member '{pointer.member}'."),
                };
            }
            if (pointer.owner != "NumberFormatInfo")
                throw new NeoScriptPreExecutionValidationError($"Unknown globalization owner '{pointer.owner}'.");
            switch (pointer.member)
            {
                case ".ctor":
                    return new NumberFormatInfo();
                case "InvariantInfo":
                    return NeoNumberFormatInfos.InvariantInfo;
                case "CurrentInfo":
                    return NeoNumberFormatInfos.CurrentInfo;
                case "GetInstance":
                    return NeoNumberFormatInfos.GetInstance(PrimitiveFormatProvider(args[0], ctx));
                case "ReadOnly":
                    return NeoNumberFormatInfos.ReadOnly((NumberFormatInfo?)args[0]);
            }
            if (receiver is not NumberFormatInfo info)
                throw new NeoScriptPreExecutionValidationError("A NumberFormatInfo member requires a number-format receiver.");
            switch (pointer.member)
            {
                case "IsReadOnly":
                    return info.IsReadOnly;
                case "Clone":
                    return NeoNumberFormatInfos.Clone(info);
                case "GetFormat":
                    return IsNumberFormatType(args[0]) ? info : null;
                case "NativeDigits":
                    return NeoNumberFormatInfos.GetNativeDigits(info);
                case "DigitSubstitution":
                    return EnumOfNumber((int)info.DigitSubstitution, NeoPrimitiveConformance.DigitShapesEnumId, ctx);
            }
            if (!numberFormatProperties.TryGetValue(pointer.member, out PropertyInfo property))
                throw new NeoScriptPreExecutionValidationError($"Unknown NumberFormatInfo member '{pointer.member}'.");
            object value = property.GetValue(info)!;
            return value is int[] sizes ? sizes.Select(size => (object?)(double)size).ToArray() : value;
        }

        private static bool IsNumberFormatType(object? value) => value is NeoTypeValue type
            && type.Binding.Equals(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo)));

        internal static void WriteGlobalizationMember(GlobalizationMemberPointer pointer, object? value, NeoScriptScope scope, Context ctx)
        {
            object? receiver = pointer.receiver is null ? null : EvalPointer(pointer.receiver, scope, ctx);
            if (receiver is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            if (pointer.owner == "CultureInfo" && pointer.member == "NumberFormat" && receiver is CultureInfo culture)
                NeoCultures.SetNumberFormat(culture, (NumberFormatInfo?)value);
            else if (pointer.owner == "NumberFormatInfo" && receiver is NumberFormatInfo info
                && numberFormatProperties.TryGetValue(pointer.member, out PropertyInfo property))
            {
                object? retainedList = pointer.member == "NativeDigits" ? value : null;
                if (pointer.member == "DigitSubstitution")
                    value = EnumNumber(value!, NeoPrimitiveConformance.DigitShapesEnumId, ctx);
                else if (property.PropertyType == typeof(int))
                    value = NeoInt32.Wrap(Number(value!));
                else if (value is not null && property.PropertyType.IsArray)
                    value = ResolveStoredCollection(value, ctx);
                NeoNumberFormatInfos.Set(info, pointer.member, value);
                if (retainedList is object?[] list)
                {
                    CaptureRuntimeList(list, ctx);
                    NeoNumberFormatInfos.RetainNativeDigitsSource(info,
                        () => (IList)ResolveStoredCollection(RuntimeCollection(list, ctx), ctx)!);
                }
            }
            else
                throw new NeoScriptPreExecutionValidationError($"Globalization member '{pointer.owner}.{pointer.member}' is not writable.");
            ctx.client.NoteGlobalizationValue(receiver);
            PublishGlobalizationWrites(receiver, ctx.client, rejectInvalidNativeDigits: true, validateOnly: ctx.client.DefersGlobalizationWrites);
        }

        private static readonly ConditionalWeakTable<object, Dictionary<string, WeakReference<PayloadMemberValue>>> globalizationRows = new();

        internal static void TrackGlobalizationPayload(PayloadMemberValue row, object value)
        {
            // Immutable singletons live for the process; retaining their slot
            // ids serves no write and would grow with every loaded project.
            if (value is CultureInfo { IsReadOnly: true } or NumberFormatInfo { IsReadOnly: true })
                return;
            void Track(object key)
            {
                var rows = globalizationRows.GetValue(key, _ => new Dictionary<string, WeakReference<PayloadMemberValue>>(StringComparer.Ordinal));
                rows[row.id] = new WeakReference<PayloadMemberValue>(row);
            }
            Track(value);
            if (value is CultureInfo { NumberFormat: { IsReadOnly: false } } culture)
                Track(culture.NumberFormat);
        }

        internal static void PublishGlobalizationWrites(object receiver, NeoClient client, bool rejectInvalidNativeDigits, bool validateOnly = false)
        {
            if (!globalizationRows.TryGetValue(receiver, out var origins))
                return;
            // Publication replaces a row and registers it again; snapshot the
            // small set of slots holding this instance before publishing.
            foreach (var origin in origins.ToArray())
            {
                bool retainedHere = false;
                foreach (NeoValueOwnership ownership in new[] { NeoValueOwnership.Save, NeoValueOwnership.Session, NeoValueOwnership.User })
                {
                    if (!client.HasWritableValue(ownership, origin.Key)
                        || !client.TryGetValue(ownership, origin.Key, out MemberValue? current)
                        || current is not PayloadMemberValue { live: { } root } payload
                        || !(ReferenceEquals(root, receiver) || root is CultureInfo culture && ReferenceEquals(culture.NumberFormat, receiver)))
                        continue;
                    retainedHere = true;
                    TrackGlobalizationPayload(payload, root);
                    if (validateOnly)
                        NeoStorablePayloads.Encode(root);
                    else
                        client.PublishHeldPayloadWrite(ownership, payload, rejectInvalidNativeDigits);
                }
                if (!retainedHere && !origin.Value.TryGetTarget(out _))
                    origins.Remove(origin.Key);
            }
        }

        private static object? LegacyNumberText(string number, string operation, object?[] args)
        {
            string member = $"{number}.{TextOpMemberName(operation)}";
            if (operation == TextOpKind.Format)
            {
                object value = TextNumberArgument(args, 0, member, number);
                string? format = TextNullableStringArgument(args, 1, member);
                return number switch
                {
                    NeoText.Int => NeoNumberFormatting.FormatInt32(NeoInt32.Wrap(Number(value)), format, null),
                    NeoText.Float => NeoNumberFormatting.FormatSingle((float)Number(value), format, null),
                    NeoText.Decimal => NeoNumberFormatting.FormatDecimal(NeoDecimalValues.Parse((string)value), format, null),
                    _ => throw new NeoScriptPreExecutionValidationError($"Unknown numeric text kind '{number}'."),
                };
            }
            string? text = TextNullableStringArgument(args, 0, member);
            if (operation == TextOpKind.Parse)
            {
                return number switch
                {
                    NeoText.Int => Box((double)NeoNumberParsing.ParseInt32(text, null, null)),
                    NeoText.Float => Box((double)NeoNumberParsing.ParseSingle(text, null, null)),
                    NeoText.Decimal => NeoDecimalValues.Format(NeoNumberParsing.ParseDecimal(text, null, null)),
                    _ => throw new NeoScriptPreExecutionValidationError($"Unknown numeric text kind '{number}'."),
                };
            }
            if (operation != TextOpKind.TryParse)
                throw new NeoScriptPreExecutionValidationError($"Unknown numeric text operation '{operation}'.");
            return number switch
            {
                NeoText.Int => NeoNumberParsing.TryParseInt32(text, null, null, out int integer) ? Box((double)integer) : null,
                NeoText.Float => NeoNumberParsing.TryParseSingle(text, null, null, out float single) ? Box((double)single) : null,
                NeoText.Decimal => NeoNumberParsing.TryParseDecimal(text, null, null, out decimal dec) ? NeoDecimalValues.Format(dec) : null,
                _ => throw new NeoScriptPreExecutionValidationError($"Unknown numeric text kind '{number}'."),
            };
        }
    }
}
