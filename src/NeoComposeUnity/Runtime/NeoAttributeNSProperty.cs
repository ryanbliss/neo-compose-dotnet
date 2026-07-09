// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Collections;
using System.Threading;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an NSProperty-typed attribute. The stored value is
    /// always null — the runtime computes the value at evaluation
    /// time by walking the IR exposed via
    /// <see cref="NSPropertyAttribute.getter"/>.
    ///
    /// <para>There is no stored-value Writable variant. Getters are derived,
    /// while <see cref="Set(object?, object?)"/> executes the optional
    /// compiled setter and lets that NeoScript mutate its own targets.
    /// <see cref="Compute"/> walks the IR via
    /// <see cref="NSGetterEvaluator"/>; <see cref="resolvedGetter"/>
    /// and <see cref="resolvedReturnTypeInfo"/> handle the
    /// <c>extendsAttributeId</c> chain so override-form NSProperty
    /// rows that omit their own <c>getter</c> / <c>returnTypeInfo</c>
    /// fall through to the parent's compiled IR.</para>
    /// </summary>
    public class NeoAttributeNSProperty
        : NeoAttribute<NSPropertyAttribute, NullAttributeValue>
    {
        public NeoAttributeNSProperty(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeNSProperty(NeoClient client, NSPropertyAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        /// <summary>
        /// The compiled getter for this attribute, walking
        /// <c>extendsAttributeId</c> when this row is an override that
        /// inherits its IR from a parent. Returns null when no
        /// ancestor has a compiled getter.
        /// </summary>
        public FunctionWithReturnType? resolvedGetter
        {
            get
            {
                if (attribute.getter is not null) return attribute.getter;
                return CustomTypeInheritance.WalkExtendsAttributeChain(
                    attribute.id,
                    id => client.TryGetAttribute(id, out Attribute? a) ? a : null,
                    a => a is NSPropertyAttribute ng ? ng.getter : null,
                    requireType: AttributeType.NSProperty);
            }
        }

        /// <summary>
        /// The compiled setter for this property, walking the override chain
        /// independently from the getter body.
        /// </summary>
        public FunctionWithReturnType? resolvedSetter
        {
            get
            {
                if (attribute.setter is not null) return attribute.setter;
                return NeoActionExecutor.ResolveCompiledSetter(attribute.id, client);
            }
        }

        /// <summary>
        /// The declared return type, walking the override chain when
        /// this row is an override that inherits its return type from
        /// a parent. Returns null if no ancestor declares one.
        /// </summary>
        public TypeInfo? resolvedReturnTypeInfo
        {
            get
            {
                if (attribute.returnTypeInfo is not null) return attribute.returnTypeInfo;
                return CustomTypeInheritance.WalkExtendsAttributeChain(
                    attribute.id,
                    id => client.TryGetAttribute(id, out Attribute? a) ? a : null,
                    a => a is NSPropertyAttribute ng ? ng.returnTypeInfo : null,
                    requireType: AttributeType.NSProperty);
            }
        }

        /// <summary>
        /// Walks the compiled IR (<see cref="resolvedGetter"/>) and
        /// returns the produced value wrapped in an
        /// <see cref="NSGetterResult"/>. Catches
        /// <see cref="NSGetterRuntimeError"/> and any other unexpected
        /// exception so callers always have something to render —
        /// matches the TS-side <c>NSPropertyValueNodeVM.result</c>
        /// pattern.
        ///
        /// <para><paramref name="thisValue"/> binds the synthetic
        /// <c>__this__</c> parameter. When omitted (the default), it's
        /// resolved by walking <see cref="NeoAttribute.parent"/> for
        /// the nearest Custom-shaped ancestor — matches the TS
        /// <c>resolveThisFromParentChain</c> behavior. Pass an
        /// explicit value to override (e.g., for tests or for
        /// project-root NSProperties with no Custom parent).</para>
        /// </summary>
        public NSGetterResult Compute(object? thisValue = null) =>
            ComputeInternal(thisValue, /*thisRow*/ null);

        /// <summary>
        /// Convenience overload that takes a value-id string and
        /// looks up the corresponding row internally. Unlike the
        /// <see cref="Compute(object?)"/> overload, this routes the
        /// <c>__this__</c> binding through the evaluator's
        /// per-context cache + reverse index — so <c>is</c>-checks
        /// against Custom types and runtime-override dispatch on
        /// <c>this</c> itself work correctly. Prefer this overload
        /// when the receiver is a known stored row; the object-only
        /// overload is for ad-hoc / synthesized records.
        /// </summary>
        public NSGetterResult Compute(string thisValueId)
        {
            if (!client.TryGetValue(ownership, thisValueId, out AttributeValue? row))
            {
                return NSGetterResult.Error(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return ComputeInternal(null, row);
        }

        /// <summary>
        /// Executes this property's compiled setter. Deferred native
        /// Functions may make the accepted invocation pending; any eventual
        /// terminal error is logged by the SDK because the original property
        /// assignment has already returned.
        /// </summary>
        public NSSetterResult Set(object? value, object? thisValue = null) =>
            SetInternal(value, thisValue, /*thisRow*/ null);

        /// <summary>
        /// Executes the setter with <c>__this__</c> bound to a stored row id.
        /// Prefer this overload from generated property accessors so runtime
        /// override dispatch and Custom <c>is</c> checks retain row identity.
        /// </summary>
        public NSSetterResult Set(string thisValueId, object? value)
        {
            if (!client.TryGetValue(ownership, thisValueId, out AttributeValue? row))
            {
                return SetterError(
                    $"thisValueId '{thisValueId}' not found in client values");
            }
            return SetInternal(value, null, row);
        }

        private NSGetterResult ComputeInternal(object? thisValue, AttributeValue? thisRow)
        {
            var getter = resolvedGetter;
            if (getter is null)
            {
                return NSGetterResult.Error(
                    "Compiled `getter` not yet available — save the code to compile it.");
            }

            // Build the Context first so we can unwrap row-based
            // bindings through its cache. Both `__root__` and
            // `__this__` need to participate in the cache so dispatch
            // on `root.Assets.X` and `this.foo` rounds-trips through
            // reference equality.
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            object? rootValue = ResolveRootValue(ctx);
            ctx = ctx.WithRoot(rootValue);

            object? boundThis = thisValue;
            if (boundThis is null && thisRow is not null)
            {
                boundThis = NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership);
            }
            if (boundThis is null)
            {
                // Walk parent chain for a row to unwrap through the cache.
                NeoAttribute? cursor = parent;
                for (int i = 0; cursor is not null && i < 32; i++)
                {
                    if (cursor.value is ObjectAttributeValue obj)
                    {
                        boundThis = NSGetterEvaluator.UnwrapRow(obj, ctx, cursor.ownership);
                        if (boundThis is not null) break;
                    }
                    cursor = cursor.parent;
                }
            }

            try
            {
                var value = NSGetterEvaluator.Evaluate(getter, ctx.WithThis(boundThis));
                return NSGetterResult.Ok(value);
            }
            catch (NSGetterRuntimeError ex)
            {
                return NSGetterResult.Error(ex.Message);
            }
            catch (System.Exception ex)
            {
                return NSGetterResult.Error($"Evaluator error: {ex.Message}");
            }
        }

        private NSSetterResult SetInternal(
            object? value,
            object? thisValue,
            AttributeValue? thisRow)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            object? rootValue = ResolveRootValue(ctx);
            ctx = ctx.WithRoot(rootValue);

            object? boundThis = ResolveThisValue(thisValue, thisRow, ctx);
            if (boundThis is null)
            {
                return SetterError("Cannot invoke setter on a null receiver.");
            }

            string effectiveAttributeId = NeoActionExecutor.ResolveEffectiveSetterAttributeId(
                client,
                attribute.id,
                boundThis,
                ctx);
            var setter = NeoActionExecutor.ResolveCompiledSetter(
                effectiveAttributeId,
                client);
            if (setter is null)
            {
                return SetterError(
                    "Compiled `setter` not yet available — add and save setter code to compile it.");
            }

            TypeInfo? returnTypeInfo = resolvedReturnTypeInfo;
            if (returnTypeInfo is null)
            {
                return SetterError(
                    "Setter return type is unavailable — save the property to compile it.");
            }

            object? normalizedValue;
            try
            {
                normalizedValue = NormalizeSetterValue(
                    value,
                    returnTypeInfo,
                    ctx);
            }
            catch (System.Exception ex)
            {
                return SetterError($"Setter value conversion failed: {ex.Message}");
            }

            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = boundThis,
                ["__root__"] = rootValue,
                ["__value__"] = normalizedValue,
            };
            NSPropertyAttribute effectiveProperty = client.TryGetAttribute(
                effectiveAttributeId, out NSPropertyAttribute? resolvedProperty)
                    ? resolvedProperty!
                    : attribute;
            var terminalLogger = new SetterTerminalLogger(effectiveProperty);
            try
            {
                var execution = NeoActionExecutor.Execute(
                    client,
                    setter,
                    scope,
                    ctx.WithSetterPushed(effectiveAttributeId).WithThis(boundThis),
                    NeoActionExecutionOptions
                        .ForUnity(client)
                        .ForProperty(effectiveAttributeId));
                if (!execution.IsPaused) return NSSetterResult.Ok();

                ObservePendingExecution(execution, terminalLogger);
                return NSSetterResult.Pending();
            }
            catch (System.Exception ex)
            {
                terminalLogger.Log(ex);
                return NSSetterResult.Error(ex.Message);
            }
        }

        private object? ResolveThisValue(
            object? thisValue,
            AttributeValue? thisRow,
            NSGetterEvaluator.Context ctx)
        {
            if (thisValue is not null) return thisValue;
            if (thisRow is not null)
            {
                return NSGetterEvaluator.UnwrapRow(thisRow, ctx, ownership);
            }
            NeoAttribute? cursor = parent;
            for (int i = 0; cursor is not null && i < 32; i++)
            {
                if (cursor.value is ObjectAttributeValue obj)
                {
                    object? resolved = NSGetterEvaluator.UnwrapRow(
                        obj,
                        ctx,
                        cursor.ownership);
                    if (resolved is not null) return resolved;
                }
                cursor = cursor.parent;
            }
            return null;
        }

        private object? NormalizeSetterValue(
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx)
        {
            if (value is NeoValueWritePayload payload)
            {
                if (payload.isValueReference)
                {
                    value = payload.valueReference
                        ?? (object?)payload.valueId;
                }
                else
                {
                    value = payload.value;
                }
            }
            if (value is null) return null;

            if (value is NeoLookupSelection selection)
            {
                if (!client.TryGetValue(selection.valueId, out AttributeValue? selectedRow))
                {
                    throw new System.InvalidOperationException(
                        $"Selected Neo value '{selection.valueId}' was not found.");
                }
                NeoValueOwnership selectedOwnership = client.TryGetValueOwnership(
                    selection.valueId, out NeoValueOwnership foundSelectionOwnership)
                        ? foundSelectionOwnership
                        : ownership;
                return NSGetterEvaluator.UnwrapRow(
                    selectedRow,
                    ctx,
                    selectedOwnership);
            }
            if (value is NeoDialogueReference dialogueReference)
            {
                return dialogueReference.Id;
            }
            string? optionId = EnumOptionId(value);
            if (optionId is not null && value is not string)
            {
                return optionId;
            }

            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                if (!client.TryGetValue(reference.valueId!, out AttributeValue? referencedRow))
                {
                    throw new System.InvalidOperationException(
                        $"Referenced Neo value '{reference.valueId}' was not found.");
                }
                NeoValueOwnership referencedOwnership = client.TryGetValueOwnership(
                    reference.valueId!, out NeoValueOwnership foundOwnership)
                        ? foundOwnership
                        : ownership;
                return NSGetterEvaluator.UnwrapRow(
                    referencedRow,
                    ctx,
                    referencedOwnership);
            }

            switch (typeInfo.type)
            {
                case AttributeType.Decimal:
                    if (value is decimal decimalValue)
                    {
                        return NeoDecimalValues.Format(decimalValue);
                    }
                    if (value is double or float or int or long or short)
                    {
                        return NSGetterEvaluator.CoerceDecimalOperand(
                            value,
                            "setter value");
                    }
                    return value;
                case AttributeType.Vector2:
                    if (value is NeoReadOnlyVector2 vector2)
                        return NeoGeneratedTypesSupport.Vector2Value(vector2.Value);
                    if (value is Vector2 unityVector2)
                        return NeoGeneratedTypesSupport.Vector2Value(unityVector2);
                    return value;
                case AttributeType.Vector2Int:
                    if (value is NeoReadOnlyVector2Int vector2Int)
                        return NeoGeneratedTypesSupport.Vector2IntValue(vector2Int.Value);
                    if (value is Vector2Int unityVector2Int)
                        return NeoGeneratedTypesSupport.Vector2IntValue(unityVector2Int);
                    return value;
                case AttributeType.Vector3:
                    if (value is NeoReadOnlyVector3 vector3)
                        return NeoGeneratedTypesSupport.Vector3Value(vector3.Value);
                    if (value is Vector3 unityVector3)
                        return NeoGeneratedTypesSupport.Vector3Value(unityVector3);
                    return value;
                case AttributeType.Vector3Int:
                    if (value is NeoReadOnlyVector3Int vector3Int)
                        return NeoGeneratedTypesSupport.Vector3IntValue(vector3Int.Value);
                    if (value is Vector3Int unityVector3Int)
                        return NeoGeneratedTypesSupport.Vector3IntValue(unityVector3Int);
                    return value;
                case AttributeType.Color:
                    if (value is NeoReadOnlyColor color)
                        return NeoGeneratedTypesSupport.ColorValue(color.Value);
                    if (value is Color unityColor)
                        return NeoGeneratedTypesSupport.ColorValue(unityColor);
                    return value;
                case AttributeType.Sprite:
                    if (value is Sprite sprite)
                        return NeoGeneratedTypesSupport.SpriteValue(client, sprite);
                    return value;
                case AttributeType.Audio:
                    if (value is AudioClip audio)
                        return NeoGeneratedTypesSupport.AudioValue(client, audio);
                    return value;
                case AttributeType.Enum:
                    return EnumOptionId(value) ?? value;
                case AttributeType.List:
                case AttributeType.Lookup:
                case AttributeType.DialogueLookup:
                    return NormalizeEnumerable(value, typeInfo, ctx);
                case AttributeType.Dictionary:
                    return NormalizeDictionary(value, typeInfo, ctx);
                default:
                    return value;
            }
        }

        private object?[] NormalizeEnumerable(
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx)
        {
            if (value is string || value is not IEnumerable enumerable)
            {
                throw new System.InvalidOperationException(
                    $"Expected an enumerable setter value for {typeInfo.type}.");
            }
            TypeInfo? entryType = typeInfo switch
            {
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            var result = new List<object?>();
            foreach (object? entry in enumerable)
            {
                result.Add(entryType is null
                    ? NormalizeSetterValue(
                        entry,
                        new UnknownTypeInfo
                        {
                            type = AttributeType.Unknown,
                            required = false,
                        },
                        ctx)
                    : NormalizeSetterValue(entry, entryType, ctx));
            }
            return result.ToArray();
        }

        private Dictionary<string, object?> NormalizeDictionary(
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx)
        {
            TypeInfo? entryType = (typeInfo as CollectionTypeInfo)?.entryTypeInfo;
            var result = new Dictionary<string, object?>();
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    AddNormalizedDictionaryEntry(
                        result,
                        entry.Key,
                        entry.Value,
                        entryType,
                        ctx);
                }
                return result;
            }

            // NeoReadOnlyDictionary<T> and the enum-keyed two-arity wrapper
            // implement generic IEnumerable<KeyValuePair<...>>, not the
            // non-generic IDictionary interface. Read their stable Key/Value
            // shape without coupling this boundary codec to every closed
            // generic wrapper type emitted by codegen.
            if (value is IEnumerable entries && value is not string)
            {
                foreach (object? entry in entries)
                {
                    if (entry is null) continue;
                    System.Type entryClrType = entry.GetType();
                    System.Reflection.PropertyInfo? keyProperty =
                        entryClrType.GetProperty("Key");
                    System.Reflection.PropertyInfo? valueProperty =
                        entryClrType.GetProperty("Value");
                    if (keyProperty is null || valueProperty is null)
                    {
                        throw new System.InvalidOperationException(
                            $"Dictionary setter entry '{entryClrType.FullName}' does not expose Key/Value properties.");
                    }
                    AddNormalizedDictionaryEntry(
                        result,
                        keyProperty.GetValue(entry),
                        valueProperty.GetValue(entry),
                        entryType,
                        ctx);
                }
                return result;
            }

            throw new System.InvalidOperationException(
                "Expected a dictionary setter value.");
        }

        private void AddNormalizedDictionaryEntry(
            Dictionary<string, object?> result,
            object? keyValue,
            object? entryValue,
            TypeInfo? entryType,
            NSGetterEvaluator.Context ctx)
        {
            string key = EnumOptionId(keyValue)
                ?? keyValue?.ToString()
                ?? "null";
            result[key] = entryType is null
                ? entryValue
                : NormalizeSetterValue(entryValue, entryType, ctx);
        }

        private static string? EnumOptionId(object? value)
        {
            if (value is string text) return text;
            System.Reflection.PropertyInfo? property = value?.GetType().GetProperty(
                "optionId",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public);
            return property?.PropertyType == typeof(string)
                ? property.GetValue(value) as string
                : null;
        }

        private NSSetterResult SetterError(string message)
        {
            Debug.LogError(
                $"NeoScript property setter '{attribute.name}' ({attribute.id}) failed: {message}");
            return NSSetterResult.Error(message);
        }

        private static void ObservePendingExecution(
            NeoActionExecutionResult execution,
            SetterTerminalLogger terminalLogger)
        {
            execution.WhenDeferredSettled(
                resumed =>
                {
                    if (resumed.IsPaused)
                    {
                        ObservePendingExecution(resumed, terminalLogger);
                    }
                },
                terminalLogger.Log);
        }

        private sealed class SetterTerminalLogger
        {
            private readonly NSPropertyAttribute property;
            private int logged;

            internal SetterTerminalLogger(NSPropertyAttribute property)
            {
                this.property = property;
            }

            internal void Log(System.Exception exception)
            {
                if (Interlocked.Exchange(ref logged, 1) != 0) return;
                Debug.LogError(
                    $"NeoScript property setter '{property.name}' ({property.id}) failed: " +
                    exception.Message);
            }
        }

        /// <summary>
        /// Synthesizes the runtime <c>__root__</c> value:
        /// <c>{ Assets: &lt;assets-record&gt;, Save: &lt;save-record&gt; }</c>.
        /// The two roots come from <see cref="NeoClient.assets"/> /
        /// <see cref="NeoClient.save"/>'s underlying value records;
        /// either entry is null when the corresponding root attribute
        /// has no stored value. Both records are unwrapped through
        /// the evaluator's cache so chains like <c>root.Assets.X</c>
        /// participate in reference-equality dispatch.
        /// </summary>
        private object? ResolveRootValue(NSGetterEvaluator.Context ctx)
        {
            var root = new Dictionary<string, object?>(3);
            root["Assets"] = client.assets.value is ObjectAttributeValue a
                ? NSGetterEvaluator.UnwrapRow(a, ctx, NeoValueOwnership.Asset)
                : null;
            root["Save"] = client.save.value is ObjectAttributeValue s
                ? NSGetterEvaluator.UnwrapRow(s, ctx, NeoValueOwnership.Save)
                : null;
            root["Session"] = client.session.value is ObjectAttributeValue ss
                ? NSGetterEvaluator.UnwrapRow(ss, ctx, NeoValueOwnership.Session)
                : null;
            return root;
        }
    }
}
