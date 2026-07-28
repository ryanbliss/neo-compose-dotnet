// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    internal readonly struct NeoGeneratedConstructorDictionaryEntry
    {
        internal NeoGeneratedConstructorDictionaryEntry(
            object? key,
            object? value)
        {
            Key = key;
            Value = value;
        }

        internal object? Key { get; }
        internal object? Value { get; }
    }

    /// <summary>
    /// Ownership-qualified reference captured from a generated wrapper or a
    /// NeoScript row-backed value. Stable ids can coexist in Session and Save,
    /// so constructor attachment must not rediscover ownership from the id
    /// after the caller has already selected a concrete value.
    /// </summary>
    internal readonly struct NeoConstructorValueReference
    {
        internal NeoConstructorValueReference(
            string valueId,
            NeoValueOwnership? ownership)
        {
            this.valueId = valueId;
            this.ownership = ownership;
        }

        internal string valueId { get; }
        internal NeoValueOwnership? ownership { get; }
    }

    /// <summary>
    /// Non-generic bridge used by generated dictionary views so constructor
    /// materialization does not need reflection for the common
    /// <c>NeoDictionary&lt;T&gt;</c> path.
    /// </summary>
    internal interface INeoGeneratedConstructorDictionary
    {
        IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateGeneratedConstructorEntries();
    }

    /// <summary>
    /// Stable generated-constructor argument descriptor. Generated facades
    /// identify each supplied value by both merged schema key and member id
    /// so stale generated code fails before any value rows are published.
    /// </summary>
    public sealed class NeoGeneratedConstructorValue
    {
        public string schemaKey { get; }
        public string memberId { get; }
        public object? value { get; }

        public NeoGeneratedConstructorValue(
            string schemaKey,
            string memberId,
            object? value)
        {
            this.schemaKey = schemaKey
                ?? throw new ArgumentNullException(nameof(schemaKey));
            this.memberId = memberId
                ?? throw new ArgumentNullException(nameof(memberId));
            this.value = value;
        }
    }

    /// <summary>
    /// Common option-id surface implemented by generated enum option classes.
    /// It lets the shared constructor materializer handle enum values without
    /// reflection or project-specific generated helpers.
    /// </summary>
    public interface INeoEnumOption
    {
        string optionId { get; }
    }

    /// <summary>
    /// Shared helper methods used by web-generated C# facade classes.
    /// Kept in the SDK runtime so generated files only contain
    /// project-specific schema wrappers.
    /// </summary>
    public static class NeoGeneratedTypesSupport
    {
        private sealed class ConstructorKeyValuePairAccessors
        {
            internal System.Reflection.PropertyInfo key = null!;
            internal System.Reflection.PropertyInfo value = null!;
        }

        private static readonly object ConstructorDictionaryShapeLock = new();
        private static readonly Dictionary<Type, bool>
            ConstructorDictionaryShapeCache = new();
        private static readonly Dictionary<Type, ConstructorKeyValuePairAccessors?>
            ConstructorKeyValuePairAccessorsCache = new();

        internal sealed class RuntimeConstructorField
        {
            internal string schemaKey = null!;
            internal string memberId = null!;
            internal object? value;
        }

        private sealed class RuntimeConstructorMetadata
        {
            internal Dictionary<string, Member> membersBySchemaKey = null!;
            internal IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv = null!;
        }

        public delegate object ReadOnlyClassFactory(
            NeoClient client,
            NeoMemberClass node);

        public delegate object WritableClassFactory(
            NeoClient client,
            NeoMemberClassWritable node);

        public static NeoValueWritePayload? Value<T>(T? value)
        {
            return NeoValueWritePayload.FromValue(value);
        }

        /// <summary>
        /// Builds a live member-id keyed static-member view. Generated
        /// properties call this from the active project singleton, so every
        /// access observes the current authored/Save/Session binding.
        /// </summary>
        public static NeoStaticBinding StaticBinding(
            NeoClient client,
            string memberId,
            NeoValueOwnership ownership)
        {
            return new NeoStaticBinding(client, memberId, ownership);
        }

        public static SpriteValue? SpriteValue(
            NeoClient client,
            Sprite? sprite,
            string? expectedTemplateId = null,
            string? memberName = null)
        {
            return sprite is null
                ? null
                : NeoAssetResolver.ValueForSprite(
                    client.assetDatabase,
                    sprite,
                    expectedTemplateId,
                    memberName);
        }

        /// <summary>
        /// Wrapper-typed sprite write funnel (P42 §4.1). Generated setters for
        /// Sprite members bind here now that the generated property type is
        /// <see cref="NeoSprite"/>: the wrapper already carries the
        /// addressable <c>fileId</c>/<c>sliceIndex</c> pair, so it is read
        /// directly rather than round-tripped through a resolved
        /// <see cref="UnityEngine.Sprite"/>, exactly as
        /// <c>SetColor</c>/<c>SetVector3</c> read their wrappers. Letting the
        /// <see cref="UnityEngine.Sprite"/> overload above win through the
        /// implicit conversion would regress twice: it throws for an
        /// unsynchronized asset on what is only a data write, and it discards
        /// <c>sliceIndex</c> whenever the sprite cannot be reverse-resolved.
        ///
        /// <para><b>Template validation is preserved.</b> Writing a sprite
        /// from the wrong sheet still throws. When the wrapper carries an
        /// addressable value the check runs against the asset-database entry
        /// for its <c>fileId</c> — no resolved <see cref="UnityEngine.Sprite"/>
        /// is needed, because the entry is what carries the template id. When
        /// the wrapper carries only a Unity sprite (the implicit
        /// <c>obj.Portrait = someUnitySprite</c> conversion, whose
        /// reverse-resolution is deliberately deferred to write time) the
        /// unchanged <c>NeoAssetResolver.ValueForSprite</c> path runs,
        /// including its untracked-sprite diagnostic.</para>
        /// </summary>
        public static SpriteValue? SpriteValue(
            NeoClient client,
            NeoReadOnlySprite? sprite,
            string? expectedTemplateId = null,
            string? memberName = null)
        {
            if (sprite is null) return null;

            // Value is always a fresh copy, so handing it to the write payload
            // cannot alias the source member's live row.
            var addressable = sprite.Value;
            if (addressable is null)
            {
                // No addressable value yet. Either the wrapper was detached
                // from a Unity sprite the asset database does not track — in
                // which case ValueForSprite raises the diagnostic callers
                // already know — or the source member simply has no row, which
                // writes null exactly as it did before P42.
                var resolved = sprite.Resolve();
                return resolved is null
                    ? null
                    : NeoAssetResolver.ValueForSprite(
                        client.assetDatabase,
                        resolved,
                        expectedTemplateId,
                        memberName);
            }

            ValidateSpriteTemplate(
                client,
                addressable.fileId,
                expectedTemplateId,
                memberName);
            return addressable;
        }

        /// <summary>
        /// Template check for a sprite write that already carries an
        /// addressable value and therefore has no resolved
        /// <see cref="UnityEngine.Sprite"/> to reverse-resolve. Mirrors the
        /// validation inside <c>NeoAssetResolver.ValueForSprite</c> — same
        /// message, same wording — so both sprite write paths report a
        /// wrong-sheet sprite identically.
        /// </summary>
        private static void ValidateSpriteTemplate(
            NeoClient client,
            string fileId,
            string? expectedTemplateId,
            string? memberName)
        {
            // No template on the member means there is nothing to validate,
            // matching NeoAssetResolver.ValidateTemplate's first guard.
            if (expectedTemplateId is null) return;

            var subject = memberName ?? "Sprite member";
            var database = client.assetDatabase ?? NeoAssetDatabase.LoadDefault();
            var entry = database?.TryGetEntry(fileId);
            if (entry is null)
            {
                // The member demands a template and the file is not in this
                // Unity project's asset database, so the check cannot run.
                // Fail loudly rather than let an unvalidated sprite reach the
                // row: an unknown file is indistinguishable from a sprite off
                // the wrong sheet, which is precisely what this guard exists
                // to catch.
                throw new InvalidOperationException(
                    $"Sprite file '{fileId}' is not synchronized into this Unity project, so it cannot be " +
                    $"validated against the Unity template required by '{subject}'. " +
                    $"Expected template id '{expectedTemplateId}'. Run Neo Compose editor sync and try again.");
            }

            if (entry.TemplateId == expectedTemplateId) return;

            var actualTemplate = entry.TemplateId ?? "<none>";
            var fileName = string.IsNullOrWhiteSpace(entry.FileName)
                ? entry.FileId
                : entry.FileName;
            throw new InvalidOperationException(
                $"Sprite '{fileName}' does not match the Unity template required by '{subject}'. " +
                $"Expected template id '{expectedTemplateId}', actual template id '{actualTemplate}'.");
        }

        public static FileValue? AudioValue(
            NeoClient client,
            AudioClip? audioClip,
            string? expectedTemplateId = null,
            string? memberName = null)
        {
            return audioClip is null
                ? null
                : NeoAssetResolver.ValueForAudioClip(
                    client.assetDatabase,
                    audioClip,
                    expectedTemplateId,
                    memberName);
        }

        public static NeoVector2Value Vector2Value(Vector2 value)
        {
            return NeoVectorValues.FromVector2(value);
        }

        public static NeoVector2Value Vector2IntValue(Vector2Int value)
        {
            return NeoVectorValues.FromVector2Int(value);
        }

        public static NeoVector3Value Vector3Value(Vector3 value)
        {
            return NeoVectorValues.FromVector3(value);
        }

        public static NeoVector3Value Vector3IntValue(Vector3Int value)
        {
            return NeoVectorValues.FromVector3Int(value);
        }

        public static NeoColorValue ColorValue(Color value)
        {
            return NeoColorValues.FromColor(value);
        }

        public static Color? ReadColorValue(object? value)
        {
            if (value is null) return null;
            if (value is Color color) return color;
            if (value is NeoReadOnlyColor wrapper) return wrapper.Value;
            if (value is NeoColorValue raw) return NeoColorValues.ToColor(raw);
            if (TryReadColorComponents(value, out float r, out float g, out float b, out float a))
            {
                return new Color(r, g, b, a);
            }
            return null;
        }

        public static Vector2? ReadVector2Value(object? value)
        {
            if (value is null) return null;
            if (value is Vector2 vector) return vector;
            if (value is NeoReadOnlyVector2 wrapper) return wrapper.Value;
            if (value is NeoVector2Value raw) return NeoVectorValues.ToVector2(raw);
            if (TryReadVectorComponents(value, false, out float x, out float y, out _))
            {
                return new Vector2(x, y);
            }
            return null;
        }

        public static Vector2Int? ReadVector2IntValue(object? value)
        {
            if (value is null) return null;
            if (value is Vector2Int vector) return vector;
            if (value is NeoReadOnlyVector2Int wrapper) return wrapper.Value;
            if (value is NeoVector2Value raw) return NeoVectorValues.ToVector2Int(raw);
            if (TryReadVectorComponents(value, false, out float x, out float y, out _))
            {
                return NeoVectorValues.ToVector2Int(new NeoVector2Value { x = x, y = y });
            }
            return null;
        }

        public static Vector3? ReadVector3Value(object? value)
        {
            if (value is null) return null;
            if (value is Vector3 vector) return vector;
            if (value is NeoReadOnlyVector3 wrapper) return wrapper.Value;
            if (value is NeoVector3Value raw) return NeoVectorValues.ToVector3(raw);
            if (TryReadVectorComponents(value, true, out float x, out float y, out float z))
            {
                return new Vector3(x, y, z);
            }
            return null;
        }

        public static Vector3Int? ReadVector3IntValue(object? value)
        {
            if (value is null) return null;
            if (value is Vector3Int vector) return vector;
            if (value is NeoReadOnlyVector3Int wrapper) return wrapper.Value;
            if (value is NeoVector3Value raw) return NeoVectorValues.ToVector3Int(raw);
            if (TryReadVectorComponents(value, true, out float x, out float y, out float z))
            {
                return NeoVectorValues.ToVector3Int(new NeoVector3Value { x = x, y = y, z = z });
            }
            return null;
        }

        public static void SetVector2(
            NeoMemberClassWritable node,
            string key,
            Vector2 value)
        {
            SetValue(node, key, Value(Vector2Value(value)));
        }

        public static void SetVector2Int(
            NeoMemberClassWritable node,
            string key,
            Vector2Int value)
        {
            SetValue(node, key, Value(Vector2IntValue(value)));
        }

        public static void SetVector3(
            NeoMemberClassWritable node,
            string key,
            Vector3 value)
        {
            SetValue(node, key, Value(Vector3Value(value)));
        }

        public static void SetVector3Int(
            NeoMemberClassWritable node,
            string key,
            Vector3Int value)
        {
            SetValue(node, key, Value(Vector3IntValue(value)));
        }

        // ------------------------------------------------------------------
        // Wrapper-typed write funnels (specs/color-member.md §4/§6.2, as
        // amended by P42 decision D6).
        //
        // Generated property setters route through these: `obj.Position = v`
        // reads the supplied wrapper's *current* value once and writes that
        // value. Assignment is therefore still a value copy — assigning
        // `a.Position = b.Position` does not link the two members, and later
        // edits to `b` do not reach `a`.
        //
        // What changed in P42 is the wrapper itself, so the old unqualified
        // "value-copy semantics, never a live link" is now only half true and
        // has to be read per binding:
        //
        //   * A DETACHED wrapper — one built from a plain value: the implicit
        //     operator, `new NeoVector3(...)`, a factory argument — owns its
        //     value. Mutating a component is local and reaches the project
        //     only when the wrapper is assigned through one of these funnels.
        //     This is the case the old comment described.
        //
        //   * A BOUND wrapper — one minted from a member node, which is what
        //     every generated getter now returns — IS a live link to its own
        //     leaf. `obj.Position.y = 1f` writes through immediately, without
        //     passing through here at all (P42 §1.2 read-modify-write, guarded
        //     by NeoStructuredLeafWriteGuard per decision D5). These funnels
        //     are only the whole-value assignment path.
        //
        // The native-typed overloads above stay for NeoScript marshalling and
        // value-row creation. The null guard throws a distinct
        // ArgumentNullException because an implicit-conversion NRE would
        // otherwise surface with a useless message.
        // ------------------------------------------------------------------

        public static void SetVector2(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector2 value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector2 wrapper to required member '{key}'.");
            }
            SetVector2(node, key, value.Value);
        }

        public static void SetVector2OrClear(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector2? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector2(node, key, value.Value);
        }

        public static void SetVector2Int(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector2Int value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector2Int wrapper to required member '{key}'.");
            }
            SetVector2Int(node, key, value.Value);
        }

        public static void SetVector2IntOrClear(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector2Int? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector2Int(node, key, value.Value);
        }

        public static void SetVector3(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector3 value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector3 wrapper to required member '{key}'.");
            }
            SetVector3(node, key, value.Value);
        }

        public static void SetVector3OrClear(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector3? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector3(node, key, value.Value);
        }

        public static void SetVector3Int(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector3Int value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector3Int wrapper to required member '{key}'.");
            }
            SetVector3Int(node, key, value.Value);
        }

        public static void SetVector3IntOrClear(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyVector3Int? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector3Int(node, key, value.Value);
        }

        public static void SetColor(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyColor value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Color wrapper to required member '{key}'.");
            }
            SetValue(node, key, Value(ColorValue(value.Value)));
        }

        public static void SetColorOrClear(
            NeoMemberClassWritable node,
            string key,
            NeoReadOnlyColor? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetValue(node, key, Value(ColorValue(value.Value)));
        }

        public static TGenerated GetOrCreateGeneratedClassValue<TGenerated>(
            NeoClient client,
            NeoMemberClass node,
            Func<TGenerated> create)
            where TGenerated : NeoGeneratedClassValue
        {
            return client.GetOrCreateGeneratedClassValue(node, create);
        }

        /// <summary>
        /// Resolves and caches an authored animation clip for a generated
        /// target value. Generated clip properties call this helper.
        /// </summary>
        public static NeoAnimationClip<T> GetAnimationClip<T>(
            T target,
            string schemaKey)
            where T : NeoGeneratedClassValue
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            return target.Client.GetOrCreateAnimationClip(target, schemaKey);
        }

        /// <summary>
        /// Core of the generated <c>GetChild&lt;T&gt;</c> family. Enumerates a generated
        /// Children collection live (no caching), returning the first child assignable
        /// to <typeparamref name="TChild"/> in list order, optionally filtered by an
        /// ordinal match on the child's <c>Name</c>. Each match is resolved to its
        /// writable twin when one exists, otherwise returned as-is.
        /// </summary>
        public static bool TryGetGeneratedChild<TChild>(
            System.Collections.IEnumerable? children,
            string? name,
            out TChild child)
            where TChild : NeoGeneratedClassValue
        {
            if (children is not null)
            {
                foreach (var item in children)
                {
                    var resolved = ResolveGeneratedChild<TChild>(item, name);
                    if (resolved is null) continue;
                    child = resolved;
                    return true;
                }
            }

            child = null!;
            return false;
        }

        /// <summary>
        /// Required variant of <see cref="TryGetGeneratedChild{TChild}"/> for children
        /// the content contract guarantees to exist.
        /// </summary>
        public static TChild GetRequiredGeneratedChild<TChild>(
            NeoGeneratedClassValue owner,
            System.Collections.IEnumerable? children,
            string? name)
            where TChild : NeoGeneratedClassValue
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            if (TryGetGeneratedChild(children, name, out TChild child))
            {
                return child;
            }

            string nameFilter = name is null ? string.Empty : $" named '{name}'";
            throw new InvalidOperationException(
                $"Generated value '{owner.GetType().Name}' (valueId '{owner.valueId}') has no child of type '{typeof(TChild).Name}'{nameFilter}.");
        }

        /// <summary>
        /// Plural variant of <see cref="TryGetGeneratedChild{TChild}"/>: every child
        /// assignable to <typeparamref name="TChild"/>, in list order.
        /// </summary>
        public static IReadOnlyList<TChild> GetGeneratedChildren<TChild>(
            System.Collections.IEnumerable? children)
            where TChild : NeoGeneratedClassValue
        {
            if (children is null) return Array.Empty<TChild>();

            var matches = new List<TChild>();
            foreach (var item in children)
            {
                var resolved = ResolveGeneratedChild<TChild>(item, name: null);
                if (resolved is null) continue;
                matches.Add(resolved);
            }
            return matches;
        }

        private static TChild? ResolveGeneratedChild<TChild>(object? item, string? name)
            where TChild : NeoGeneratedClassValue
        {
            if (item is not NeoGeneratedClassValue value) return null;

            TChild? typed;
            if (value.TryWritable(out TChild writable))
            {
                typed = writable;
            }
            else
            {
                typed = value as TChild;
            }
            if (typed is null) return null;

            if (name is not null
                && !string.Equals(ReadGeneratedName(typed), name, StringComparison.Ordinal))
            {
                return null;
            }

            return typed;
        }

        private static string? ReadGeneratedName(NeoGeneratedClassValue value)
        {
            var nameProperty = value.GetType().GetProperty("Name", typeof(string));
            if (nameProperty is null || !nameProperty.CanRead) return null;
            return nameProperty.GetValue(value) as string;
        }

        public static object? ResolveClassValue(
            NeoClient client,
            string valueId,
            IReadOnlyDictionary<string, ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableClassFactory> savedFactories)
        {
            if (!client.TryGetValue(valueId, out ObjectMemberValue? value))
            {
                return null;
            }
            string? classId = ResolveClassValueClassId(client, valueId, value);
            if (string.IsNullOrEmpty(classId)) return null;

            ClassMember member;
            if (TryInferMemberForValueId(
                    client,
                    valueId,
                    new HashSet<string>(),
                    out Member? inferredMember)
                && inferredMember is ClassMember inferredClassMember)
            {
                member = inferredClassMember;
            }
            else
            {
                member = new ClassMember
                {
                    id = $"__neo_resolved_class_{classId}",
                    name = "ResolvedClassValue",
                    kind = MemberKind.Class,
                    classId = classId,
                    createdAt = value.createdAt,
                    updatedAt = value.updatedAt,
                };
            }

            if (client.TryGetValueOwnership(valueId, out NeoValueOwnership ownership)
                && (ownership == NeoValueOwnership.Save || ownership == NeoValueOwnership.Session)
                && savedFactories.TryGetValue(classId, out var savedFactory))
            {
                return savedFactory(
                    client,
                    new NeoMemberClassWritable(client, member, valueId, ownership));
            }

            if (readOnlyFactories.TryGetValue(classId, out var readOnlyFactory))
            {
                return readOnlyFactory(
                    client,
                    new NeoMemberClass(client, member, valueId));
            }

            return null;
        }

        public static T ResolveNativeFunctionReceiver<T>(
            NeoClient client,
            object? receiver,
            IReadOnlyDictionary<string, ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableClassFactory> savedFactories,
            string functionName,
            string memberId)
            where T : class
        {
            if (receiver is T typed) return typed;
            string? valueId = ValueId(receiver);
            if (!string.IsNullOrEmpty(valueId))
            {
                var resolved = ResolveClassValue(
                    client,
                    valueId!,
                    readOnlyFactories,
                    savedFactories);
                if (resolved is T resolvedTyped) return resolvedTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Cannot invoke Function '{functionName}' ({memberId}) because receiver type '{receiver?.GetType().Name ?? "null"}' is not supported.");
        }

        public static T? ResolveNativeFunctionClassArgument<T>(
            NeoClient client,
            object? value,
            bool required,
            IReadOnlyDictionary<string, ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableClassFactory> savedFactories,
            string argumentName)
            where T : class
        {
            if (value is null)
            {
                if (required)
                {
                    throw new NeoScript.NSGetterRuntimeError(
                        $"Native Function argument '{argumentName}' is required.");
                }
                return null;
            }
            if (value is T typed) return typed;
            string? valueId = ValueId(value);
            if (!string.IsNullOrEmpty(valueId))
            {
                var resolved = ResolveClassValue(
                    client,
                    valueId!,
                    readOnlyFactories,
                    savedFactories);
                if (resolved is T resolvedTyped) return resolvedTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Native Function argument '{argumentName}' could not be converted to {typeof(T).Name}.");
        }

        public static TDeferred ResolveDeferredFunction<TDeferred>(
            NeoDeferredFunctionBase deferred,
            string functionName)
            where TDeferred : NeoDeferredFunctionBase
        {
            if (deferred is TDeferred typed) return typed;
            var expectedType = typeof(TDeferred);
            if (expectedType == typeof(NeoDeferredFunction))
            {
                return (TDeferred)(NeoDeferredFunctionBase)new NeoDeferredFunction(
                    deferred.StateCore);
            }
            if (expectedType.IsGenericType
                && expectedType.GetGenericTypeDefinition() == typeof(NeoDeferredFunction<>))
            {
                var created = Activator.CreateInstance(
                    expectedType,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { deferred.StateCore },
                    culture: null);
                if (created is TDeferred createdTyped) return createdTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Deferred Function '{functionName}' expected handle type {expectedType.Name}, got {deferred.GetType().Name}.");
        }

        private static string? ResolveClassValueClassId(
            NeoClient client,
            string valueId,
            ObjectMemberValue value)
        {
            if (!string.IsNullOrEmpty(value.classId)) return value.classId;
            return TryInferNeoSchemaClassId(
                client,
                valueId,
                new HashSet<string>(),
                out string? classId)
                ? classId
                : null;
        }

        private static bool TryInferNeoSchemaClassId(
            NeoClient client,
            string valueId,
            HashSet<string> visitingValueIds,
            out string? classId)
        {
            if (!visitingValueIds.Add(valueId))
            {
                classId = null;
                return false;
            }

            if (client.TryGetValue(valueId, out ObjectMemberValue? value)
                && !string.IsNullOrEmpty(value.classId))
            {
                classId = value.classId;
                return true;
            }

            if (TryInferMemberForValueId(
                    client,
                    valueId,
                    visitingValueIds,
                    out Member? member)
                && member != null
                && TryResolveDirectNeoSchemaClassIdFromMember(member, out classId))
            {
                return true;
            }

            classId = null;
            return false;
        }

        private static bool TryInferMemberForValueId(
            NeoClient client,
            string valueId,
            HashSet<string> visitingValueIds,
            out Member? member)
        {
            foreach (var candidate in client.members.Values)
            {
                if (candidate.valueId == valueId)
                {
                    member = candidate;
                    return true;
                }
            }

            foreach (var parent in EnumerateValues(client))
            {
                if (parent.Value is not ObjectMemberValue objectValue
                    || objectValue.value == null)
                {
                    continue;
                }

                foreach (var pair in objectValue.value)
                {
                    if (pair.Value != valueId) continue;
                    if (TryInferMemberForValueId(
                            client,
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out Member? parentMember)
                        && TryResolveCollectionEntryMember(
                            client,
                            parentMember,
                            out Member? parentEntryMember))
                    {
                        member = parentEntryMember;
                        return true;
                    }

                    if (!TryInferNeoSchemaClassId(
                            client,
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out string? parentClassId)
                        || string.IsNullOrEmpty(parentClassId)
                        || !client.classes.TryGetValue(parentClassId, out NeoSchemaClass? parentClass))
                    {
                        continue;
                    }

                    MergedSchemaEntry? matchedEntry = null;
                    foreach (MergedSchemaEntry entry in NeoSchemaClassInheritance.MergeStoredInstanceSchema(
                        NeoSchemaClassInheritance.ResolveChain(
                            parentClass.id,
                            id => client.TryGetClass(id, out NeoSchemaClass? candidate)
                                ? candidate
                                : null),
                        id => client.TryGetMember(id, out Member? candidate)
                            ? candidate
                            : null))
                    {
                        if (entry.schemaKey == pair.Key)
                        {
                            matchedEntry = entry;
                            break;
                        }
                    }
                    if (matchedEntry is null
                        || !client.TryGetMember(
                            matchedEntry.memberId,
                            out Member? childMember))
                    {
                        continue;
                    }

                    member = childMember;
                    return true;
                }
            }

            foreach (var parent in EnumerateValues(client))
            {
                if (parent.Value is ArrayMemberValue arrayValue
                    && arrayValue.value != null
                    && Contains(arrayValue.value, valueId)
                    && TryInferMemberForValueId(
                        client,
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out Member? collectionMember)
                    && TryResolveCollectionEntryMember(
                        client,
                        collectionMember,
                        out Member? entryMember))
                {
                    member = entryMember;
                    return true;
                }

                if (parent.Value is ObjectMemberValue dictionaryValue
                    && dictionaryValue.value != null
                    && dictionaryValue.value.ContainsValue(valueId)
                    && TryInferMemberForValueId(
                        client,
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out collectionMember)
                    && TryResolveCollectionEntryMember(
                        client,
                        collectionMember,
                        out entryMember))
                {
                    member = entryMember;
                    return true;
                }
            }

            member = null;
            return false;
        }

        private static bool TryResolveDirectNeoSchemaClassIdFromMember(
            Member member,
            out string? classId)
        {
            if (member is ClassMember classMember
                && !string.IsNullOrEmpty(classMember.classId))
            {
                classId = classMember.classId;
                return true;
            }

            classId = null;
            return false;
        }

        private static bool TryResolveCollectionEntryMember(
            NeoClient client,
            Member? member,
            out Member? entryMember)
        {
            string? entryMemberId = member switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => null,
            };
            if (member is LookupMember lookup
                && client.TryGetMember(
                    lookup.collectionMemberId,
                    out Member? collectionMember))
            {
                return TryResolveCollectionEntryMember(
                    client,
                    collectionMember,
                    out entryMember);
            }

            if (string.IsNullOrEmpty(entryMemberId)
                || !client.TryGetMember(entryMemberId!, out Member? resolved))
            {
                entryMember = null;
                return false;
            }

            entryMember = resolved;
            return true;
        }

        private static IEnumerable<KeyValuePair<string, MemberValue>> EnumerateValues(
            NeoClient client)
        {
            foreach (var pair in client.sessionValues) yield return pair;
            foreach (var pair in client.saveValues) yield return pair;
            foreach (var pair in client.values) yield return pair;
        }

        private static bool Contains(string[] values, string value)
        {
            foreach (var item in values)
            {
                if (item == value) return true;
            }
            return false;
        }

        private static bool TryReadVectorComponents(
            object value,
            bool zRequired,
            out float x,
            out float y,
            out float z)
        {
            x = 0;
            y = 0;
            z = 0;
            if (value is IDictionary<string, object?> dict)
            {
                if (dict.Count != (zRequired ? 3 : 2)) return false;
                return TryReadFloat(dict.TryGetValue("x", out var xv) ? xv : null, out x)
                    && TryReadFloat(dict.TryGetValue("y", out var yv) ? yv : null, out y)
                    && (!zRequired || TryReadFloat(dict.TryGetValue("z", out var zv) ? zv : null, out z));
            }
            if (value is JObject obj)
            {
                if (obj.Count != (zRequired ? 3 : 2)) return false;
                return TryReadFloat(obj["x"], out x)
                    && TryReadFloat(obj["y"], out y)
                    && (!zRequired || TryReadFloat(obj["z"], out z));
            }
            return false;
        }

        private static bool TryReadColorComponents(
            object value,
            out float r,
            out float g,
            out float b,
            out float a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 0;
            if (value is IDictionary<string, object?> dict)
            {
                if (dict.Count != 4) return false;
                return TryReadFloat(dict.TryGetValue("r", out var rv) ? rv : null, out r)
                    && TryReadFloat(dict.TryGetValue("g", out var gv) ? gv : null, out g)
                    && TryReadFloat(dict.TryGetValue("b", out var bv) ? bv : null, out b)
                    && TryReadFloat(dict.TryGetValue("a", out var av) ? av : null, out a);
            }
            if (value is JObject obj)
            {
                if (obj.Count != 4) return false;
                return TryReadFloat(obj["r"], out r)
                    && TryReadFloat(obj["g"], out g)
                    && TryReadFloat(obj["b"], out b)
                    && TryReadFloat(obj["a"], out a);
            }
            return false;
        }

        private static bool TryReadFloat(object? value, out float result)
        {
            switch (value)
            {
                case float f:
                    result = f;
                    return !float.IsNaN(f) && !float.IsInfinity(f);
                case double d:
                    result = (float)d;
                    return !float.IsNaN(result) && !float.IsInfinity(result);
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case JValue token:
                    return TryReadFloat(token.Value, out result);
                default:
                    result = 0;
                    return false;
            }
        }

        public static NeoValueWritePayload? ValueReference(
            INeoValueReference? value)
        {
            return value is null
                ? null
                : NeoValueWritePayload.FromValueReference(
                    LookupSelectionId(value.valueId),
                    value);
        }

        /// <summary>
        /// Deep-clones a generated Class value into a new parentless Session
        /// graph. The returned writable node preserves the source's runtime
        /// Class while every owned row has a fresh value id.
        /// </summary>
        public static NeoMemberClassWritable CloneClassValue(
            NeoClient client,
            INeoValueReference source)
        {
            if (source is null || string.IsNullOrEmpty(source.valueId))
            {
                throw new ArgumentNullException(
                    nameof(source),
                    "Cannot clone a Class value without a backing value id.");
            }
            NeoValueOwnership sourceOwnership = source is NeoGeneratedClassValue generated
                ? generated.ValueOwnership
                : (client.TryGetValueOwnership(source.valueId!, out var inferredOwnership)
                    ? inferredOwnership
                    : NeoValueOwnership.Asset);
            string clonedValueId = client.CloneValueReference(
                source.valueId!,
                sourceOwnership);
            if (!client.TryGetValue(clonedValueId, out ObjectMemberValue? clone))
            {
                throw new InvalidOperationException(
                    $"Cloned Class value '{clonedValueId}' has no object value row.");
            }
            string? clonedClassId = ResolveClassValueClassId(client, clonedValueId, clone);
            if (string.IsNullOrEmpty(clonedClassId))
            {
                throw new InvalidOperationException(
                    $"Cloned Class value '{clonedValueId}' has no resolvable runtime classId.");
            }
            var factoryMember = new ClassMember
            {
                id = $"__neo_clone_class_{clonedClassId}",
                name = "Clone",
                kind = MemberKind.Class,
                classId = clonedClassId!,
                createdAt = clone.createdAt,
                updatedAt = clone.updatedAt,
            };
            return new NeoMemberClassWritable(
                client,
                factoryMember,
                clonedValueId,
                NeoValueOwnership.Session);
        }

        public static void SetValue(
            NeoMemberClassWritable node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerializedValue(key, value);
        }

        /// <summary>
        /// Writable view over a (possibly read-only) Class node. Generated
        /// classes use the overload with an inherited ownership context when
        /// inherited members should resolve storage from the concrete owner.
        /// </summary>
        public static NeoMemberClassWritable AsWritable(NeoMemberClass node)
        {
            return node.AsWritableView();
        }

        public static NeoMemberClassWritable AsWritable(
            NeoMemberClass node,
            NeoValueOwnership inheritedOwnership)
        {
            return node.AsWritableView(inheritedOwnership);
        }

        public static void SetValue(
            NeoMemberDictionaryWritable node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(key, value);
        }

        public static void AddValue(
            NeoMemberListWritable node,
            NeoValueWritePayload? value)
        {
            node.AddSerialized(value);
        }

        public static void SetValue(
            NeoMemberListWritable node,
            int index,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(index, value);
        }

        public static NeoMemberClassWritable CreateWritableClassValue(
            NeoClient client,
            string classId,
            Dictionary<string, string> value,
            IReadOnlyList<MemberValue> valueRows)
        {
            return CreateWritableClassValueCore(
                client,
                classId,
                value,
                valueRows,
                referenceOwnershipByPath: null);
        }

        private static NeoMemberClassWritable CreateWritableClassValueCore(
            NeoClient client,
            string classId,
            Dictionary<string, string> value,
            IReadOnlyList<MemberValue> valueRows,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            ValidateConstructibleNeoSchemaClass(client, classId);
            var nowIso = DateTime.UtcNow.ToString("o");
            var rows = new List<MemberValue>(valueRows);
            var parentRow = CreateWritableClassValueRow(
                client,
                classId,
                value,
                rows,
                nowIso,
                new HashSet<string>());
            rows.Add(parentRow);

            PrepareConstructedGraph(
                client,
                parentRow,
                rows,
                referenceOwnershipByPath);

            foreach (var row in rows)
            {
                client.SetWritableValue(NeoValueOwnership.Session, row);
            }

            var factoryMember = new ClassMember
            {
                id = $"__neo_factory_class_{classId}",
                name = "Factory",
                kind = MemberKind.Class,
                classId = classId,
                createdAt = nowIso,
                updatedAt = nowIso,
            };
            return new NeoMemberClassWritable(
                client,
                factoryMember,
                parentRow.id,
                NeoValueOwnership.Session);
        }

        /// <summary>
        /// Materializes generated public-constructor arguments through the
        /// same recursive, atomic supplied-value path as NeoScript
        /// <c>new Class(...)</c>. Optional null arguments are omitted, while
        /// null entries inside collections retain their position/key as an
        /// explicit nullable value row.
        /// </summary>
        public static NeoMemberClassWritable CreateWritableClassValue(
            NeoClient client,
            string classId,
            params NeoGeneratedConstructorValue[] suppliedValues)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (classId is null)
                throw new ArgumentNullException(nameof(classId));
            suppliedValues ??= Array.Empty<NeoGeneratedConstructorValue>();
            var fields = new List<RuntimeConstructorField>(suppliedValues.Length);
            foreach (NeoGeneratedConstructorValue supplied in suppliedValues)
            {
                if (supplied is null)
                {
                    throw new ArgumentException(
                        "Generated constructor arguments cannot contain null descriptors.",
                        nameof(suppliedValues));
                }
                fields.Add(new RuntimeConstructorField
                {
                    schemaKey = supplied.schemaKey,
                    memberId = supplied.memberId,
                    value = supplied.value,
                });
            }
            return CreateSuppliedClassValue(
                client,
                new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = classId,
                },
                fields,
                value => GeneratedValueReference(client, value));
        }

        private static NeoConstructorValueReference?
            GeneratedValueReference(NeoClient client, object? value)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                NeoValueOwnership? ownership = value is NeoGeneratedClassValue generated
                    ? generated.ValueOwnership
                    : client.TryGetValueOwnership(
                        reference.valueId!,
                        out NeoValueOwnership inferred)
                            ? inferred
                            : null;
                return new NeoConstructorValueReference(
                    reference.valueId!,
                    ownership);
            }
            if (value is NeoValueWritePayload payload
                && payload.isValueReference)
            {
                string? valueId = payload.valueReference?.valueId
                    ?? payload.valueId;
                if (string.IsNullOrEmpty(valueId)) return null;
                NeoValueOwnership? ownership =
                    payload.valueReference is NeoGeneratedClassValue generated
                        ? generated.ValueOwnership
                        : client.TryGetValueOwnership(
                            valueId!,
                            out NeoValueOwnership inferred)
                                ? inferred
                                : null;
                return new NeoConstructorValueReference(
                    valueId!,
                    ownership);
            }
            return null;
        }

        private sealed class PendingConstructorReference
        {
            internal string sourceValueId = null!;
            internal NeoValueOwnership sourceOwnership;
            internal Member member = null!;
            internal string path = null!;
            internal string? expectedMapKey;
            internal string? expectedContainerId;
            internal Action<string> replaceValueId = null!;
        }

        private static void ValidateConstructibleNeoSchemaClass(
            NeoClient client,
            string classId)
        {
            if (!client.TryGetClass(classId, out NeoSchemaClass? schemaClass))
            {
                throw new InvalidOperationException(
                    $"Cannot construct missing Class '{classId}'.");
            }
            if (schemaClass!.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot construct abstract Class '{schemaClass.name}'.");
            }
            if (client.TryResolveSchemaClassAllowedOwnership(
                    classId,
                    out NeoValueOwnership ownership)
                && ownership == NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    $"Cannot construct immutable-only Class '{schemaClass.name}'.");
            }
            // Also validates inheritance, closed generic bindings, and merged
            // schema integrity before any Session row can be published.
            _ = ResolveMergedSchema(client, classId);
        }

        /// <summary>
        /// Validates and normalizes the complete generated/runtime constructor
        /// graph before publication. Existing owned Class references use the
        /// ordinary Session import funnel, while every freshly staged row is
        /// schema-shaped, singly owned, and partition-stamped first.
        /// </summary>
        private static void PrepareConstructedGraph(
            NeoClient client,
            ObjectMemberValue root,
            List<MemberValue> rows,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            if (!string.IsNullOrEmpty(root.mapKey))
            {
                throw new InvalidOperationException(
                    $"Parentless constructed Class root '{root.id}' cannot arrive pre-stamped with partition '{root.mapKey}'.");
            }
            root.mapKey = null;
            var stagedById = new Dictionary<string, MemberValue>();
            foreach (MemberValue row in rows)
            {
                if (string.IsNullOrEmpty(row.id))
                {
                    throw new InvalidOperationException(
                        "Constructed value graph contains a row without an id.");
                }
                if (!stagedById.TryAdd(row.id, row))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph contains duplicate row id '{row.id}'.");
                }
                if (client.TryGetValue(row.id, out MemberValue? _))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph row id '{row.id}' collides with an existing value.");
                }
            }

            var reachableStagedIds = new HashSet<string> { root.id };
            var ownedByPath = new Dictionary<string, string>();
            var pending = new List<PendingConstructorReference>();
            ValidateConstructedClassRow(
                client,
                root,
                root.classId
                    ?? throw new InvalidOperationException(
                        "Constructed Class root has no runtime classId."),
                stagedById,
                reachableStagedIds,
                ownedByPath,
                pending,
                path: root.classId!,
                new HashSet<string>(),
                referenceOwnershipByPath);

            foreach (string stagedId in stagedById.Keys)
            {
                if (!reachableStagedIds.Contains(stagedId))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph contains orphan staged row '{stagedId}'.");
                }
            }

            // Preflight every ownership decision before the first import. This
            // keeps an already-owned reference error from leaving earlier
            // imported rows behind.
            foreach (PendingConstructorReference reference in pending)
            {
                NeoValueOwnership ownership;
                if (referenceOwnershipByPath is not null
                    && referenceOwnershipByPath.TryGetValue(
                        reference.path,
                        out NeoValueOwnership suppliedOwnership))
                {
                    ownership = suppliedOwnership;
                    if (!client.TryGetValue(
                            ownership,
                            reference.sourceValueId,
                            out MemberValue? _))
                    {
                        throw new InvalidOperationException(
                            $"Constructed field '{reference.path}' references missing {ownership} value '{reference.sourceValueId}'.");
                    }
                }
                else if (!client.TryGetValueOwnership(
                             reference.sourceValueId,
                             out ownership))
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{reference.path}' references missing value '{reference.sourceValueId}'.");
                }
                reference.sourceOwnership = ownership;
                if (ownership == NeoValueOwnership.Session
                    && client.TryFindOwnedParent(
                        ownership,
                        reference.sourceValueId,
                        out string? parentValueId))
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{reference.path}' cannot attach value '{reference.sourceValueId}' because it is already owned by parent value '{parentValueId}'. Call Clone() explicitly before constructing another owner.");
                }
            }

            var newlyImportedRoots = new List<string>();
            try
            {
                foreach (PendingConstructorReference reference in pending)
                {
                    bool existedInSession =
                        reference.sourceOwnership == NeoValueOwnership.Session
                        && client.HasWritableValue(
                            NeoValueOwnership.Session,
                            reference.sourceValueId);
                    string importedValueId = reference.sourceOwnership
                        == NeoValueOwnership.Session
                            ? client.ImportValueReference(
                                NeoValueOwnership.Session,
                                reference.sourceValueId)
                            : client.CloneOwnedValueReferenceForNewParent(
                                NeoValueOwnership.Session,
                                reference.sourceOwnership,
                                reference.sourceValueId,
                                reference.member);
                    reference.replaceValueId(importedValueId);
                    if ((reference.sourceOwnership != NeoValueOwnership.Session
                            || !existedInSession)
                        && client.HasWritableValue(
                            NeoValueOwnership.Session,
                            importedValueId))
                    {
                        newlyImportedRoots.Add(importedValueId);
                    }
                    StampImportedConstructorGraph(
                        client,
                        importedValueId,
                        reference.member,
                        reference.expectedMapKey,
                        new HashSet<string>(),
                        expectedContainerId: reference.expectedContainerId);
                }
            }
            catch
            {
                foreach (string importedValueId in newlyImportedRoots)
                {
                    client.RemoveTemporaryWritableValueGraph(
                        NeoValueOwnership.Session,
                        importedValueId);
                }
                throw;
            }
        }

        private static void ValidateConstructedClassRow(
            NeoClient client,
            ObjectMemberValue row,
            string classId,
            IReadOnlyDictionary<string, MemberValue> stagedById,
            HashSet<string> reachableStagedIds,
            Dictionary<string, string> ownedByPath,
            List<PendingConstructorReference> pending,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            if (!traversal.Add(row.id))
            {
                throw new InvalidOperationException(
                    $"Constructed value graph contains an owned cycle at '{path}'/'{row.id}'.");
            }
            try
            {
                if (!IsAssignableNeoSchemaClass(client, classId, classId))
                {
                    throw new InvalidOperationException(
                    $"Constructed Class row '{row.id}' has unknown runtime class '{classId}'.");
                }
                if (row.value is null)
                {
                    throw new InvalidOperationException(
                        $"Constructed Class root '{path}' cannot have a null record payload.");
                }
                IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                    client,
                    classId);
                var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
                foreach (MergedSchemaEntry entry in schema)
                {
                    if (!schemaByKey.TryAdd(entry.schemaKey, entry))
                    {
                        throw new InvalidOperationException(
                            $"Merged schema for '{classId}' contains duplicate key '{entry.schemaKey}'.");
                    }
                }
                foreach (string key in row.value.Keys)
                {
                    if (!schemaByKey.ContainsKey(key))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Class row '{path}' contains unknown schema key '{key}'.");
                    }
                }

                var env = NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    classId,
                    classArguments: null);
                foreach (MergedSchemaEntry entry in schema)
                {
                    if (!client.TryGetMember(
                            entry.memberId,
                            out Member? rawMember))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Class row '{path}' schema key '{entry.schemaKey}' references missing member '{entry.memberId}'.");
                    }
                    Member member = NeoGenericResolution.SubstituteMember(
                        client,
                        rawMember,
                        env);
                    if (!IsStoredConstructorMember(member))
                    {
                        if (row.value.ContainsKey(entry.schemaKey))
                        {
                            if (member.isReadOnly == true)
                            {
                                throw new InvalidOperationException(
                                    $"Constructed Class row '{path}' contains read-only declaration member '{entry.schemaKey}'; read-only declaration members cannot have instance values.");
                            }
                            throw new InvalidOperationException(
                                $"Constructed Class row '{path}' contains non-stored member '{entry.schemaKey}'.");
                        }
                        continue;
                    }
                    if (!row.value.TryGetValue(entry.schemaKey, out string? childId))
                    {
                        if (member.required)
                        {
                            throw new InvalidOperationException(
                                $"Constructed Class row '{path}' is missing required member '{entry.schemaKey}'/'{entry.memberId}'.");
                        }
                        continue;
                    }
                    if (string.IsNullOrEmpty(childId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Class row '{path}.{entry.schemaKey}' references an empty value id.");
                    }
                    string key = entry.schemaKey;
                    ValidateConstructedValueLink(
                        client,
                        member,
                        childId,
                        replacement => row.value[key] = replacement,
                        row.mapKey,
                        classId,
                        stagedById,
                        reachableStagedIds,
                        ownedByPath,
                        pending,
                        $"{path}.{entry.schemaKey}",
                        traversal,
                        env,
                        referenceOwnershipByPath);
                }
            }
            finally
            {
                traversal.Remove(row.id);
            }
        }

        private static void ValidateConstructedValueLink(
            NeoClient client,
            Member member,
            string valueId,
            Action<string> replaceValueId,
            string? parentMapKey,
            string? parentClassId,
            IReadOnlyDictionary<string, MemberValue> stagedById,
            HashSet<string> reachableStagedIds,
            Dictionary<string, string> ownedByPath,
            List<PendingConstructorReference> pending,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath,
            string? expectedContainerId = null)
        {
            if (ownedByPath.TryGetValue(valueId, out string? priorPath))
            {
                throw new InvalidOperationException(
                    $"Constructed value '{valueId}' would have two owned parents ('{priorPath}' and '{path}'). Call Clone() explicitly for a second owner.");
            }
            ownedByPath[valueId] = path;
            string? expectedMapKey = client.ResolveCreatedValueMapKey(
                member,
                parentMapKey,
                parentClassId);

            if (!stagedById.TryGetValue(valueId, out MemberValue? row))
            {
                if (member is not ClassMember classMember)
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{path}' references unstaged value '{valueId}' for non-Class member '{member.id}'.");
                }
                bool sourceExists = referenceOwnershipByPath is not null
                    && referenceOwnershipByPath.TryGetValue(
                        path,
                        out NeoValueOwnership suppliedOwnership)
                        ? client.TryGetValue(
                            suppliedOwnership,
                            valueId,
                            out ObjectMemberValue? source)
                        : client.TryGetValue(
                            valueId,
                            out source);
                if (!sourceExists)
                {
                    throw new InvalidOperationException(
                        $"Constructed Class field '{path}' references missing object value '{valueId}'.");
                }
                string actualClassId = source!.classId ?? classMember.classId;
                if (!IsAssignableNeoSchemaClass(
                        client,
                        actualClassId,
                        classMember.classId))
                {
                    throw new InvalidOperationException(
                        $"Constructed Class field '{path}' expects '{classMember.classId}' but value '{valueId}' has runtime class '{actualClassId}'.");
                }
                if (!MapKeyCanMoveTo(source.mapKey, expectedMapKey))
                {
                    throw new InvalidOperationException(
                        $"Constructed Class field '{path}' cannot attach value '{valueId}' from partition '{source.mapKey ?? "main"}' to '{expectedMapKey ?? "main"}'.");
                }
                pending.Add(new PendingConstructorReference
                {
                    sourceValueId = valueId,
                    member = member,
                    path = path,
                    expectedMapKey = expectedMapKey,
                    expectedContainerId = expectedContainerId,
                    replaceValueId = replaceValueId,
                });
                return;
            }

            reachableStagedIds.Add(valueId);
            if (!MapKeyCanMoveTo(row.mapKey, expectedMapKey))
            {
                throw new InvalidOperationException(
                    $"Constructed field '{path}' carries partition '{row.mapKey ?? "main"}' but resolves to '{expectedMapKey ?? "main"}'.");
            }
            row.mapKey = expectedMapKey;
            if (expectedContainerId is not null)
            {
                if (!string.IsNullOrEmpty(row.containerId)
                    && row.containerId != expectedContainerId)
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{path}' already belongs to unordered list '{row.containerId}', expected '{expectedContainerId}'.");
                }
                row.containerId = expectedContainerId;
            }
            ValidateConstructedRowShape(client, member, row, path);

            switch (member)
            {
                case ClassMember classMember
                    when row is ObjectMemberValue classRow
                    && classRow.value is not null:
                {
                    string actualClassId = classRow.classId
                        ?? classMember.classId;
                    if (!IsAssignableNeoSchemaClass(
                            client,
                            actualClassId,
                            classMember.classId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Class field '{path}' expects '{classMember.classId}' but staged row '{valueId}' has runtime class '{actualClassId}'.");
                    }
                    classRow.classId = actualClassId;
                    ValidateConstructedClassRow(
                        client,
                        classRow,
                        actualClassId,
                        stagedById,
                        reachableStagedIds,
                        ownedByPath,
                        pending,
                        path,
                        traversal,
                        referenceOwnershipByPath);
                    break;
                }
                case ListMember listMember
                    when row is ArrayMemberValue listRow
                    && listRow.value is not null:
                {
                    if (!client.TryGetMember(
                            listMember.entryMemberId,
                            out Member? entryMember))
                    {
                        throw new InvalidOperationException(
                            $"Constructed List field '{path}' references missing entry member '{listMember.entryMemberId}'.");
                    }
                    entryMember = NeoGenericResolution.SubstituteMember(
                        client,
                        entryMember,
                        env);
                    bool isUnordered = client.IsUnorderedList(listMember);
                    var memberIds = new List<string>(listRow.value);
                    if (isUnordered)
                    {
                        // A low-level generated constructor may already carry
                        // canonical unordered membership on staged rows. The
                        // shared runtime materializer temporarily carries ids
                        // inline so external Class references can participate
                        // in the same ownership validation before publication.
                        foreach (MemberValue stagedRow in stagedById.Values)
                        {
                            if (stagedRow.containerId == listRow.id
                                && !memberIds.Contains(stagedRow.id))
                            {
                                memberIds.Add(stagedRow.id);
                            }
                        }
                    }
                    for (int index = 0; index < memberIds.Count; index++)
                    {
                        int capturedIndex = index;
                        ValidateConstructedValueLink(
                            client,
                            entryMember,
                            memberIds[index],
                            isUnordered
                                ? _ => { }
                                : replacement => listRow.value[capturedIndex] = replacement,
                            listRow.mapKey,
                            listRow.classId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            pending,
                            $"{path}[{index}]",
                            traversal,
                            env,
                            referenceOwnershipByPath,
                            expectedContainerId: isUnordered
                                ? listRow.id
                                : null);
                    }
                    if (isUnordered)
                    {
                        // Unordered List payload is only the present/null
                        // discriminator; membership lives on entry rows.
                        listRow.value = Array.Empty<string>();
                    }
                    break;
                }
                case DictionaryMember dictionaryMember
                    when row is ObjectMemberValue dictionaryRow
                    && dictionaryRow.value is not null:
                {
                    if (!client.TryGetMember(
                            dictionaryMember.entryMemberId,
                            out Member? entryMember))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Dictionary field '{path}' references missing entry member '{dictionaryMember.entryMemberId}'.");
                    }
                    entryMember = NeoGenericResolution.SubstituteMember(
                        client,
                        entryMember,
                        env);
                    foreach (string key in new List<string>(dictionaryRow.value.Keys))
                    {
                        string capturedKey = key;
                        ValidateConstructedValueLink(
                            client,
                            entryMember,
                            dictionaryRow.value[key],
                            replacement => dictionaryRow.value[capturedKey] = replacement,
                            dictionaryRow.mapKey,
                            dictionaryRow.classId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            pending,
                            $"{path}[{key}]",
                            traversal,
                            env,
                            referenceOwnershipByPath);
                    }
                    break;
                }
            }
        }

        private static void ValidateConstructedRowShape(
            NeoClient client,
            Member member,
            MemberValue row,
            string path)
        {
            bool shapeMatches = member switch
            {
                NullMember => row is NullMemberValue,
                BoolMember => row is BoolMemberValue,
                IntMember => row is NumberMemberValue number
                    && (number.value is null
                        || number.value.Value == Math.Truncate(number.value.Value)),
                FloatMember => row is NumberMemberValue,
                StringMember or DecimalMember => row is StringMemberValue,
                DictionaryMember or ClassMember => row is ObjectMemberValue,
                ListMember or EnumMember or LookupMember or DialogueLookupMember =>
                    row is ArrayMemberValue,
                SpriteMember => row is SpriteMemberValue,
                AudioMember => row is FileMemberValue,
                Vector2Member or Vector2IntMember => row is Vector2MemberValue,
                Vector3Member or Vector3IntMember => row is Vector3MemberValue,
                ColorMember => row is ColorMemberValue,
                _ => false,
            };
            if (!shapeMatches)
            {
                throw new InvalidOperationException(
                    $"Constructed field '{path}' has row shape '{row.GetType().Name}', incompatible with schema member '{member.id}' ({member.kind}).");
            }
            if (member.required && IsNullStoredValue(row))
            {
                throw new InvalidOperationException(
                    $"Constructed required field '{path}' has a null value.");
            }
            if (member is DecimalMember
                && row is StringMemberValue decimalRow
                && decimalRow.value is not null
                && NeoDecimalValues.GetViolation(decimalRow.value)
                    != NeoDecimalValues.Violation.None)
            {
                throw new InvalidOperationException(
                    $"Constructed Decimal field '{path}' is not a canonical decimal value.");
            }
            if (member is ClassMember classMember
                && row is ObjectMemberValue classRow)
            {
                string actualClassId = classRow.classId
                    ?? classMember.classId;
                if (!IsAssignableNeoSchemaClass(
                        client,
                        actualClassId,
                        classMember.classId))
                {
                    throw new InvalidOperationException(
                        $"Constructed Class field '{path}' has incompatible runtime class '{actualClassId}'.");
                }
            }
        }

        private static bool IsNullStoredValue(MemberValue row)
        {
            return row switch
            {
                NullMemberValue => true,
                BoolMemberValue value => value.value is null,
                NumberMemberValue value => value.value is null,
                StringMemberValue value => value.value is null,
                ArrayMemberValue value => value.value is null,
                ObjectMemberValue value => value.value is null,
                SpriteMemberValue value => value.value is null,
                FileMemberValue value => value.value is null,
                Vector2MemberValue value => value.value is null,
                Vector3MemberValue value => value.value is null,
                ColorMemberValue value => value.value is null,
                _ => true,
            };
        }

        private static bool IsAssignableNeoSchemaClass(
            NeoClient client,
            string actualClassId,
            string expectedClassId)
        {
            if (!client.TryGetClass(actualClassId, out NeoSchemaClass? _)) return false;
            try
            {
                foreach (NeoSchemaClass schemaClass in NeoSchemaClassInheritance.ResolveChain(
                    actualClassId,
                    id => client.TryGetClass(id, out NeoSchemaClass? candidate)
                        ? candidate
                        : null))
                {
                    if (schemaClass.id == expectedClassId) return true;
                }
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            return false;
        }

        private static bool MapKeyCanMoveTo(
            string? currentMapKey,
            string? expectedMapKey)
        {
            return string.IsNullOrEmpty(currentMapKey)
                || currentMapKey == expectedMapKey;
        }

        private static void StampImportedConstructorGraph(
            NeoClient client,
            string valueId,
            Member member,
            string? expectedMapKey,
            HashSet<string> visited,
            bool requireValue = true,
            string? expectedContainerId = null)
        {
            if (!visited.Add(valueId)) return;
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    valueId,
                    out MemberValue? row))
            {
                // Stable-id authored aggregates may contain sparse child
                // references whose value row resolves through the schema
                // default rather than a stored row. Ordinary assignment/import
                // preserves those ids; constructor attachment must do the same.
                if (!requireValue) return;
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' is missing from Session storage.");
            }
            if (!MapKeyCanMoveTo(row!.mapKey, expectedMapKey))
            {
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' has partition '{row.mapKey ?? "main"}', expected '{expectedMapKey ?? "main"}'.");
            }
            if (!string.IsNullOrEmpty(row.containerId)
                && expectedContainerId is not null
                && row.containerId != expectedContainerId)
            {
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' already belongs to unordered list '{row.containerId}', expected '{expectedContainerId}'.");
            }
            if (row.mapKey != expectedMapKey
                || (expectedContainerId is not null
                    && row.containerId != expectedContainerId))
            {
                MemberValue writable = client.CloneRowForWrite(row);
                writable.mapKey = expectedMapKey;
                if (expectedContainerId is not null)
                {
                    writable.containerId = expectedContainerId;
                }
                client.SetWritableValue(NeoValueOwnership.Session, writable);
                row = writable;
            }

            switch (member)
            {
                case ClassMember classMember
                    when row is ObjectMemberValue classRow
                    && classRow.value is not null:
                {
                    string actualClassId = classRow.classId
                        ?? classMember.classId;
                    IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                        client,
                        actualClassId);
                    var env = NeoGenericResolution.ResolveInstanceEnv(
                        client,
                        actualClassId,
                        classArguments: null);
                    foreach (MergedSchemaEntry entry in schema)
                    {
                        if (!classRow.value.TryGetValue(
                                entry.schemaKey,
                                out string? childId)
                            || !client.TryGetMember(
                                entry.memberId,
                                out Member? childMember))
                        {
                            continue;
                        }
                        childMember = NeoGenericResolution.SubstituteMember(
                            client,
                            childMember,
                            env);
                        if (!IsStoredConstructorMember(childMember)) continue;
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            childMember,
                            row.mapKey,
                            actualClassId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            childMember,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
                }
                case ListMember listMember
                    when row is ArrayMemberValue listRow
                    && listRow.value is not null
                    && client.TryGetMember(
                        listMember.entryMemberId,
                        out Member? entryMember):
                    foreach (string childId in listRow.value)
                    {
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            entryMember,
                            row.mapKey,
                            row.classId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            entryMember,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
                case DictionaryMember dictionaryMember
                    when row is ObjectMemberValue dictionaryRow
                    && dictionaryRow.value is not null
                    && client.TryGetMember(
                        dictionaryMember.entryMemberId,
                        out Member? entryMember):
                    foreach (string childId in dictionaryRow.value.Values)
                    {
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            entryMember,
                            row.mapKey,
                            row.classId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            entryMember,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
            }
        }

        /// <summary>
        /// Materializes the shared NeoScript <c>classConstructor</c>
        /// intrinsic through the same Session-backed value graph used by
        /// generated public C# constructors. Explicit fields are applied by
        /// schema/member id; ordinary required defaults are then filled by
        /// <see cref="CreateWritableClassValue"/>.
        /// </summary>
        internal static NeoMemberClassWritable CreateRuntimeClassValue(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            return CreateSuppliedClassValue(
                client,
                classTypeInfo,
                fields,
                valueReference);
        }

        private static NeoMemberClassWritable CreateSuppliedClassValue(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            RuntimeConstructorMetadata metadata =
                ValidateRuntimeClassConstructorMetadataCore(
                    client,
                    classTypeInfo,
                    fields);

            var value = new Dictionary<string, string>();
            var rows = new List<MemberValue>();
            var referenceOwnershipByPath =
                new Dictionary<string, NeoValueOwnership>();
            string nowIso = DateTime.UtcNow.ToString("o");
            foreach (RuntimeConstructorField field in fields)
            {
                Member member = metadata.membersBySchemaKey[field.schemaKey];
                if (field.value is null
                    && !RequiresRuntimeConstructorArgument(member))
                {
                    // Matches generated C# optional parameters: null means the
                    // field is omitted and its ordinary constructor/default
                    // behavior applies.
                    continue;
                }
                string? fieldValueId = MaterializeRuntimeConstructorValue(
                    client,
                    member,
                    field.value,
                    rows,
                    nowIso,
                    valueReference,
                    metadata.genericEnv,
                    $"{classTypeInfo.classId}.{field.schemaKey}",
                    referenceOwnershipByPath);
                if (fieldValueId is not null)
                {
                    value[field.schemaKey] = fieldValueId;
                }
            }
            return CreateWritableClassValueCore(
                client,
                classTypeInfo.classId,
                value,
                rows,
                referenceOwnershipByPath);
        }

        /// <summary>
        /// Validates all class/type-info/member metadata carried by constructor IR
        /// without inspecting argument values. The evaluator invokes this
        /// before evaluating any argument pointer, matching NeoScript's
        /// compile-time call-shape ordering and preventing stale IR from
        /// running argument side effects.
        /// </summary>
        internal static void ValidateRuntimeClassConstructorMetadata(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields)
        {
            ValidateRuntimeClassConstructorMetadataCore(
                client,
                classTypeInfo,
                fields);
        }

        private static RuntimeConstructorMetadata
            ValidateRuntimeClassConstructorMetadataCore(
                NeoClient client,
                ClassTypeInfo classTypeInfo,
                IReadOnlyList<RuntimeConstructorField> fields)
        {
            if (!client.TryGetClass(classTypeInfo.classId, out NeoSchemaClass? schemaClass))
            {
                throw new InvalidOperationException(
                    $"NeoScript construction references missing class '{classTypeInfo.classId}'.");
            }
            if (schemaClass!.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot construct abstract class '{schemaClass.name}'.");
            }
            if (classTypeInfo.type != MemberKind.Class)
            {
                throw new InvalidOperationException(
                    $"Class constructor for '{classTypeInfo.classId}' carries non-class runtime kind metadata '{classTypeInfo.type}'.");
            }
            if (client.TryResolveSchemaClassAllowedOwnership(
                    classTypeInfo.classId,
                    out NeoValueOwnership allowedOwnership)
                && allowedOwnership == NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    $"Cannot construct immutable-only class '{schemaClass.name}'.");
            }
            var genericEnv = NeoGenericResolution.ResolveInstanceEnv(
                client,
                classTypeInfo.classId,
                classArguments: null);
            string? unboundParamId = NeoGenericResolution.FirstUnboundParamId(
                genericEnv);
            if (unboundParamId is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot construct open generic class '{schemaClass.name}'; generic param '{unboundParamId}' is unbound. Construct a closed named descendant.");
            }
            ValidateRuntimeConstructorTypeArguments(
                client,
                classTypeInfo,
                genericEnv);
            IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                client,
                classTypeInfo.classId);
            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            var membersBySchemaKey = new Dictionary<string, Member>();
            foreach (MergedSchemaEntry entry in schema)
            {
                if (!schemaByKey.TryAdd(entry.schemaKey, entry))
                {
                    throw new InvalidOperationException(
                        $"Class constructor schema for '{classTypeInfo.classId}' contains duplicate merged key '{entry.schemaKey}'.");
                }
            }

            var suppliedSchemaKeys = new HashSet<string>();
            foreach (RuntimeConstructorField field in fields)
            {
                if (!suppliedSchemaKeys.Add(field.schemaKey))
                {
                    throw new InvalidOperationException(
                        $"Class constructor for '{classTypeInfo.classId}' contains duplicate field '{field.schemaKey}'.");
                }
            }
            foreach (MergedSchemaEntry entry in schema)
            {
                if (!client.TryGetMember(entry.memberId, out Member? member))
                {
                    throw new InvalidOperationException(
                        $"Class constructor schema field '{entry.schemaKey}' references missing member '{entry.memberId}'.");
                }
                member = NeoGenericResolution.SubstituteMember(
                    client,
                    member,
                    genericEnv);
                membersBySchemaKey[entry.schemaKey] = member;
                if (!IsStoredConstructorMember(member)) continue;
                if (RequiresRuntimeConstructorArgument(member)
                    && !suppliedSchemaKeys.Contains(entry.schemaKey))
                {
                    throw new InvalidOperationException(
                        $"Class constructor for '{classTypeInfo.classId}' is missing required field '{entry.schemaKey}'/'{entry.memberId}'. Regenerate the NeoScript IR from the current schema.");
                }
            }
            foreach (RuntimeConstructorField field in fields)
            {
                if (!schemaByKey.TryGetValue(field.schemaKey, out MergedSchemaEntry? entry)
                    || entry.memberId != field.memberId)
                {
                    throw new InvalidOperationException(
                        $"Class constructor for '{classTypeInfo.classId}' contains stale field '{field.schemaKey}'/'{field.memberId}'. Regenerate the NeoScript IR from the current schema.");
                }
                Member member = membersBySchemaKey[field.schemaKey];
                if (!IsStoredConstructorMember(member))
                {
                    if (member.isReadOnly == true)
                    {
                        throw new InvalidOperationException(
                            $"Class constructor field '{field.schemaKey}' references read-only declaration member '{entry.memberId}'. Regenerate the NeoScript IR; readonly fields are never constructor parameters.");
                    }
                    throw new InvalidOperationException(
                        $"Class constructor field '{field.schemaKey}' references non-stored member '{entry.memberId}'.");
                }
            }
            return new RuntimeConstructorMetadata
            {
                membersBySchemaKey = membersBySchemaKey,
                genericEnv = genericEnv,
            };
        }

        private static void ValidateRuntimeConstructorTypeArguments(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv)
        {
            if (classTypeInfo.typeArguments is null) return;
            foreach (var pair in classTypeInfo.typeArguments)
            {
                if (!genericEnv.TryGetValue(pair.Key, out NeoGenericEnvEntry? binding)
                    || !binding.IsBound
                    || string.IsNullOrEmpty(binding.memberId))
                {
                    throw new InvalidOperationException(
                        $"Class constructor type argument '{pair.Key}' is not a bound parameter of closed class '{classTypeInfo.classId}'.");
                }
                if (!client.TryGetMember(binding.memberId!, out Member? bindingMember))
                {
                    throw new InvalidOperationException(
                        $"Class constructor type argument '{pair.Key}' references missing binding member '{binding.memberId}'.");
                }
                if (!RuntimeConstructorTypeMatchesMember(
                        client,
                        pair.Value,
                        bindingMember))
                {
                    throw new InvalidOperationException(
                        $"Class constructor type argument '{pair.Key}' does not match closed class '{classTypeInfo.classId}' binding member '{binding.memberId}'.");
                }
            }
        }

        private static bool RuntimeConstructorTypeMatchesMember(
            NeoClient client,
            TypeInfo typeInfo,
            Member member)
        {
            if (typeInfo.type != member.kind
                || typeInfo.required != member.required)
            {
                return false;
            }
            if (typeInfo is ClassTypeInfo classType
                && member is ClassMember classMember)
            {
                return classType.classId == classMember.classId;
            }
            if (typeInfo is EnumTypeInfo enumType
                && member is EnumMember enumMember)
            {
                return enumType.enumId == enumMember.enumId;
            }
            if (typeInfo is CollectionTypeInfo collectionType
                && member is ListMember listMember
                && client.TryGetMember(
                    listMember.entryMemberId,
                    out Member? listEntry))
            {
                return RuntimeConstructorTypeMatchesMember(
                    client,
                    collectionType.entryTypeInfo,
                    listEntry);
            }
            if (typeInfo is CollectionTypeInfo dictionaryType
                && member is DictionaryMember dictionaryMember
                && client.TryGetMember(
                    dictionaryMember.entryMemberId,
                    out Member? dictionaryEntry))
            {
                return RuntimeConstructorTypeMatchesMember(
                    client,
                    dictionaryType.entryTypeInfo,
                    dictionaryEntry);
            }
            return true;
        }

        private static bool RequiresRuntimeConstructorArgument(
            Member member)
        {
            return member.required && !HasExplicitDefaultValue(member);
        }

        private static bool IsStoredConstructorMember(Member member)
        {
            return !member.isStatic
                && member.isReadOnly != true
                && member is not NSPropertyMember
                && member is not FunctionMember
                && member is not NSFunctionMember;
        }

        private static bool HasExplicitDefaultValue(Member schemaMember)
        {
            return schemaMember switch
            {
                NullMember member => member.defaultValue is not null,
                BoolMember member => member.defaultValue is not null,
                IntMember member => member.defaultValue is not null,
                FloatMember member => member.defaultValue is not null,
                StringMember member => member.defaultValue is not null,
                DictionaryMember member => member.defaultValue is not null,
                ListMember member => member.defaultValue is not null,
                ClassMember member => member.defaultValue is not null,
                GenericMember member => member.defaultValue is not null,
                EnumMember member => member.defaultValue is not null,
                LookupMember member => member.defaultValue is not null,
                DialogueLookupMember member => member.defaultValue is not null,
                SpriteMember member => member.defaultValue is not null,
                AudioMember member => member.defaultValue is not null,
                Vector2Member member => member.defaultValue is not null,
                Vector2IntMember member => member.defaultValue is not null,
                Vector3Member member => member.defaultValue is not null,
                Vector3IntMember member => member.defaultValue is not null,
                ColorMember member => member.defaultValue is not null,
                DecimalMember member => member.defaultValue is not null,
                _ => false,
            };
        }

        private static string? MaterializeRuntimeConstructorValue(
            NeoClient client,
            Member member,
            object? runtimeValue,
            List<MemberValue> rows,
            string nowIso,
            Func<object?, NeoConstructorValueReference?> valueReference,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            string path,
            Dictionary<string, NeoValueOwnership> referenceOwnershipByPath,
            bool preserveOptionalNull = false)
        {
            if (runtimeValue is null)
            {
                if (member.required)
                {
                    throw new InvalidOperationException(
                        $"Required constructor field '{member.name}' received null.");
                }
                if (!preserveOptionalNull) return null;
            }

            if (runtimeValue is not null && member is ClassMember)
            {
                NeoConstructorValueReference? source = valueReference(runtimeValue);
                if (source is null || string.IsNullOrEmpty(source.Value.valueId))
                {
                    throw new InvalidOperationException(
                        $"Class constructor field '{member.name}' is not backed by a Neo value.");
                }
                // Ownership import is deliberately deferred until the entire
                // staged constructor graph has passed schema/shape validation.
                // PrepareConstructedGraph then applies the same ordinary
                // parentless-attach / already-owned rejection rule used by
                // generated C# constructors and normal assignments.
                if (source.Value.ownership is NeoValueOwnership ownership)
                {
                    referenceOwnershipByPath[path] = ownership;
                }
                return source.Value.valueId;
            }

            NeoValuePayload? wrappedPayload = runtimeValue
                is INeoValuePayloadProvider provider
                    ? provider.ToNeoValuePayload()
                    : null;
            object? suppliedValue = wrappedPayload?.value ?? runtimeValue;
            if (suppliedValue is null && member.required)
            {
                throw new InvalidOperationException(
                    $"Required constructor field '{member.name}' received null.");
            }
            bool materializeExplicitNull = suppliedValue is null;
            object? payload = suppliedValue;
            string valueId = Guid.NewGuid().ToString();
            if (materializeExplicitNull)
            {
                // A null optional field is normally omitted. Once it appears
                // inside a collection, however, it is a real element and must
                // become the correctly typed null row (including Array/Object
                // rows for enum, lookup, nested list, and nested dictionary
                // entries) so list positions and dictionary keys are stable.
                payload = null;
            }
            else if (member is ListMember listMember)
            {
                if (!client.TryGetMember(
                        listMember.entryMemberId,
                        out Member? entryMember))
                {
                    throw new InvalidOperationException(
                        $"List constructor field '{member.name}' references missing entry member '{listMember.entryMemberId}'.");
                }
                entryMember = NeoGenericResolution.SubstituteMember(
                    client,
                    entryMember,
                    genericEnv);
                var ids = new List<string>();
                if (suppliedValue is System.Collections.IEnumerable enumerable
                    && suppliedValue is not string)
                {
                    foreach (object? item in enumerable)
                    {
                        string? id = MaterializeRuntimeConstructorValue(
                            client,
                            entryMember,
                            item,
                            rows,
                            nowIso,
                            valueReference,
                            genericEnv,
                            $"{path}[{ids.Count}]",
                            referenceOwnershipByPath,
                            preserveOptionalNull: true);
                        if (id is null)
                        {
                            throw new InvalidOperationException(
                                $"List constructor field '{member.name}' failed to materialize an entry.");
                        }
                        ids.Add(id);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"List constructor field '{member.name}' requires a collection value.");
                }
                payload = ids.ToArray();
            }
            else if (member is DictionaryMember dictionaryMember)
            {
                if (!client.TryGetMember(
                        dictionaryMember.entryMemberId,
                        out Member? entryMember))
                {
                    throw new InvalidOperationException(
                        $"Dictionary constructor field '{member.name}' references missing entry member '{dictionaryMember.entryMemberId}'.");
                }
                entryMember = NeoGenericResolution.SubstituteMember(
                    client,
                    entryMember,
                    genericEnv);
                if (!TryEnumerateConstructorDictionary(
                        suppliedValue!,
                        out IEnumerable<NeoGeneratedConstructorDictionaryEntry>?
                            dictionaryEntries))
                {
                    throw new InvalidOperationException(
                        $"Dictionary constructor field '{member.name}' requires a dictionary value.");
                }
                var ids = new Dictionary<string, string>();
                foreach (NeoGeneratedConstructorDictionaryEntry pair in
                         dictionaryEntries)
                {
                    string key = pair.Key switch
                    {
                        INeoEnumOption option => option.optionId,
                        string text => text,
                        null => throw new InvalidOperationException(
                            $"Dictionary constructor field '{member.name}' contains a null key."),
                        _ => pair.Key.ToString()
                            ?? throw new InvalidOperationException(
                                $"Dictionary constructor field '{member.name}' contains an invalid key."),
                    };
                    string? id = MaterializeRuntimeConstructorValue(
                        client,
                        entryMember,
                        pair.Value,
                        rows,
                        nowIso,
                        valueReference,
                        genericEnv,
                        $"{path}[{key}]",
                        referenceOwnershipByPath,
                        preserveOptionalNull: true);
                    if (id is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary constructor field '{member.name}' failed to materialize key '{key}'.");
                    }
                    ids[key] = id;
                }
                payload = ids;
            }
            else if (member is EnumMember enumMember)
            {
                payload = ConstructorEnumOptionIds(
                    suppliedValue!, enumMember);
            }
            else if (member is LookupMember lookupMember)
            {
                payload = ConstructorLookupIds(suppliedValue!, lookupMember);
            }
            else if (member is DialogueLookupMember dialogueMember)
            {
                payload = ConstructorDialogueIds(
                    suppliedValue!, dialogueMember);
            }
            else
            {
                payload = NormalizeGeneratedConstructorScalar(
                    client,
                    member,
                    suppliedValue);
            }

            if (wrappedPayload is not null)
            {
                payload = new NeoValuePayload(
                    payload,
                    wrappedPayload.classId,
                    wrappedPayload.valueRows);
            }

            if (payload is NeoValuePayload finalWrappedPayload)
            {
                rows.AddRange(finalWrappedPayload.valueRows);
            }
            MemberValue row = MemberValueFactory.Create(
                member,
                payload,
                valueId,
                nowIso,
                nowIso);
            NeoGenericResolution.StampGenericBindings(
                client,
                member,
                row,
                genericEnv);
            rows.Add(row);
            return valueId;
        }

        private static string[] ConstructorEnumOptionIds(
            object runtimeValue,
            EnumMember member)
        {
            if (runtimeValue is string optionId)
            {
                return new[] { optionId };
            }
            if (runtimeValue is INeoEnumOption option)
            {
                return new[] { option.optionId };
            }
            if (runtimeValue is not System.Collections.IEnumerable values)
            {
                throw new InvalidOperationException(
                    $"Enum constructor field '{member.name}' requires an enum option or option collection.");
            }
            var optionIds = new List<string>();
            foreach (object? value in values)
            {
                string? id = value switch
                {
                    string text => text,
                    INeoEnumOption enumOption => enumOption.optionId,
                    _ => null,
                };
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException(
                        $"Enum constructor field '{member.name}' contains an invalid option.");
                }
                optionIds.Add(id!);
            }
            string[] result = optionIds.ToArray();
            ValidateConstructorSelectionCardinality(
                result,
                member.multiselect,
                member.name,
                "Enum");
            return result;
        }

        private static bool TryEnumerateConstructorDictionary(
            object value,
            out IEnumerable<NeoGeneratedConstructorDictionaryEntry> entries)
        {
            if (value is INeoGeneratedConstructorDictionary generated)
            {
                entries = generated.EnumerateGeneratedConstructorEntries();
                return true;
            }
            if (value is System.Collections.IDictionary dictionary)
            {
                entries = EnumerateNonGenericConstructorDictionary(dictionary);
                return true;
            }
            if (value is System.Collections.IEnumerable enumerable
                && IsGenericStringKeyDictionary(value.GetType()))
            {
                entries = EnumerateGenericConstructorDictionary(enumerable);
                return true;
            }

            entries = Array.Empty<NeoGeneratedConstructorDictionaryEntry>();
            return false;
        }

        private static IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateNonGenericConstructorDictionary(
                System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry pair in dictionary)
            {
                yield return new NeoGeneratedConstructorDictionaryEntry(
                    pair.Key,
                    pair.Value);
            }
        }

        private static bool IsGenericStringKeyDictionary(Type type)
        {
            lock (ConstructorDictionaryShapeLock)
            {
                if (ConstructorDictionaryShapeCache.TryGetValue(
                        type,
                        out bool cached))
                {
                    return cached;
                }
            }

            bool matches = false;
            foreach (Type contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType) continue;
                Type definition = contract.GetGenericTypeDefinition();
                if ((definition == typeof(IDictionary<,>)
                        || definition == typeof(IReadOnlyDictionary<,>))
                    && contract.GetGenericArguments()[0] == typeof(string))
                {
                    matches = true;
                    break;
                }
            }
            lock (ConstructorDictionaryShapeLock)
            {
                ConstructorDictionaryShapeCache[type] = matches;
            }
            return matches;
        }

        private static IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateGenericConstructorDictionary(
                System.Collections.IEnumerable dictionary)
        {
            foreach (object? pair in dictionary)
            {
                if (pair is null)
                {
                    throw new InvalidOperationException(
                        "Generated constructor dictionary yielded a null entry.");
                }
                ConstructorKeyValuePairAccessors? accessors =
                    ConstructorDictionaryAccessors(pair.GetType());
                if (accessors is null)
                {
                    throw new InvalidOperationException(
                        $"Generated constructor dictionary yielded unsupported entry type '{pair.GetType().FullName}'.");
                }
                yield return new NeoGeneratedConstructorDictionaryEntry(
                    accessors.key.GetValue(pair),
                    accessors.value.GetValue(pair));
            }
        }

        private static ConstructorKeyValuePairAccessors?
            ConstructorDictionaryAccessors(Type pairType)
        {
            lock (ConstructorDictionaryShapeLock)
            {
                if (ConstructorKeyValuePairAccessorsCache.TryGetValue(
                        pairType,
                        out ConstructorKeyValuePairAccessors? cached))
                {
                    return cached;
                }
            }

            ConstructorKeyValuePairAccessors? result = null;
            if (pairType.IsGenericType
                && pairType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                && pairType.GetGenericArguments()[0] == typeof(string))
            {
                System.Reflection.PropertyInfo? key = pairType.GetProperty("Key");
                System.Reflection.PropertyInfo? value = pairType.GetProperty("Value");
                if (key is not null && value is not null)
                {
                    result = new ConstructorKeyValuePairAccessors
                    {
                        key = key,
                        value = value,
                    };
                }
            }
            lock (ConstructorDictionaryShapeLock)
            {
                ConstructorKeyValuePairAccessorsCache[pairType] = result;
            }
            return result;
        }

        private static string[] ConstructorLookupIds(
            object runtimeValue,
            LookupMember member)
        {
            var ids = ConstructorReferenceIds(
                runtimeValue,
                value => value switch
                {
                    NeoLookupSelection selection => selection.valueId,
                    INeoValueReference reference => reference.valueId,
                    string id => id,
                    _ => null,
                },
                $"Lookup constructor field '{member.name}'");
            ValidateConstructorSelectionCardinality(
                ids,
                member.multiselect,
                member.name,
                "Lookup");
            return ids;
        }

        private static string[] ConstructorDialogueIds(
            object runtimeValue,
            DialogueLookupMember member)
        {
            var ids = ConstructorReferenceIds(
                runtimeValue,
                value => value switch
                {
                    NeoDialogueReference reference => reference.Id,
                    string id => id,
                    _ => null,
                },
                $"DialogueLookup constructor field '{member.name}'");
            ValidateConstructorSelectionCardinality(
                ids,
                member.multiselect,
                member.name,
                "DialogueLookup");
            return ids;
        }

        private static string[] ConstructorReferenceIds(
            object runtimeValue,
            Func<object?, string?> valueId,
            string subject)
        {
            string? singleId = valueId(runtimeValue);
            if (!string.IsNullOrEmpty(singleId)) return new[] { singleId! };
            if (runtimeValue is string
                || runtimeValue is not System.Collections.IEnumerable values)
            {
                throw new InvalidOperationException(
                    $"{subject} requires a reference or reference collection.");
            }
            var ids = new List<string>();
            foreach (object? value in values)
            {
                string? id = valueId(value);
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException(
                        $"{subject} contains an unbound reference.");
                }
                ids.Add(id!);
            }
            return ids.ToArray();
        }

        private static void ValidateConstructorSelectionCardinality(
            string[] ids,
            bool multiselect,
            string memberName,
            string kind)
        {
            if (!multiselect && ids.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{kind} constructor field '{memberName}' requires exactly one selection.");
            }
        }

        private static object? NormalizeGeneratedConstructorScalar(
            NeoClient client,
            Member member,
            object? value)
        {
            switch (member)
            {
                case SpriteMember sprite when value is Sprite unitySprite:
                    return SpriteValue(
                        client,
                        unitySprite,
                        sprite.templateId,
                        sprite.name);
                case AudioMember audio when value is AudioClip unityAudio:
                    return AudioValue(
                        client,
                        unityAudio,
                        audio.templateId,
                        audio.name);
                case DecimalMember when value is double or float or int or long or short:
                    return NeoScript.NSGetterEvaluator.CoerceDecimalOperand(
                        value,
                        $"constructor field '{member.name}'");
                default:
                    return value;
            }
        }

        private static ObjectMemberValue CreateWritableClassValueRow(
            NeoClient client,
            string classId,
            Dictionary<string, string>? providedValue,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null)
        {
            if (!classStack.Add(classId))
            {
                throw new InvalidOperationException(
                    $"Recursive default class value creation detected for class '{classId}'.");
            }
            try
            {
                var value = providedValue is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(providedValue);

                var mergedSchema = ResolveMergedSchema(client, classId, classArguments);
                // Chain env overlaid with the owning slot's constructed
                // arguments (specs/class-generics.md §4.1) — an
                // instance of the declared open class binds its params
                // through the slot, not a named subclass's chain.
                var env = NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    classId,
                    classArguments);
                foreach (var entry in mergedSchema)
                {
                    if (value.ContainsKey(entry.schemaKey)) continue;
                    if (!client.TryGetMember(entry.memberId, out Member? member))
                    {
                        throw new InvalidOperationException(
                            $"Class '{classId}' schema key '{entry.schemaKey}' references missing member '{entry.memberId}'.");
                    }
                    // Generic slots substitute to their binding before the
                    // required check and default construction — required and
                    // defaultValue travel with the binding
                    // (specs/class-generics.md Decision 10).
                    member = NeoGenericResolution.SubstituteMember(client, member, env);
                    if (!IsStoredConstructorMember(member)) continue;
                    if (!member.required) continue;

                    var defaultRow = CreateDefaultValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        classStack,
                        env);
                    if (defaultRow is null) continue;

                    rows.Add(defaultRow);
                    value[entry.schemaKey] = defaultRow.id;
                }

                return new ObjectMemberValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = value,
                    classId = classId,
                };
            }
            finally
            {
                classStack.Remove(classId);
            }
        }

        private static IList<MergedSchemaEntry> ResolveMergedSchema(
            NeoClient client,
            string classId,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null)
        {
            if (!client.TryGetClass(classId, out NeoSchemaClass? schemaClass))
            {
                throw new InvalidOperationException(
                    $"Cannot create default class value for missing class '{classId}'.");
            }
            if (schemaClass.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot create default class value for abstract class '{schemaClass.name}'.");
            }
            // Instantiability: every param must be bound by the chain OR the
            // owning slot's constructed arguments — `GenericTest<Color>` is
            // instantiable even though the named class is open
            // (specs/class-generics.md §3.4).
            string? unboundParamId = NeoGenericResolution.FirstUnboundParamId(
                NeoGenericResolution.ResolveInstanceEnv(client, classId, classArguments));
            if (unboundParamId is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot create default class value for open generic class '{schemaClass.name}': generic param '{unboundParamId}' is unbound — every generic param must be bound before instantiation (specs/class-generics.md Decision 6).");
            }
            return NeoSchemaClassInheritance.MergeInstanceSchema(
                NeoSchemaClassInheritance.ResolveChain(
                    classId,
                    id => client.TryGetClass(id, out NeoSchemaClass? match)
                        ? match
                        : null),
                id => client.TryGetMember(id, out Member? member)
                    ? member
                    : null);
        }

        private static MemberValue? CreateDefaultValueRow(
            NeoClient client,
            Member schemaMember,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            switch (schemaMember)
            {
                case NullMember member:
                    return member.defaultValue is null
                        ? null
                        : CreateNullValueRow(nowIso, member.defaultValue.classId);
                case BoolMember member:
                    return member.defaultValue is null
                        ? null
                        : new BoolMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = member.defaultValue.value,
                            classId = member.defaultValue.classId,
                        };
                case IntMember member:
                    return member.defaultValue is null
                        ? null
                        : new NumberMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = member.defaultValue.value,
                            classId = member.defaultValue.classId,
                        };
                case FloatMember member:
                    return member.defaultValue is null
                        ? null
                        : new NumberMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = member.defaultValue.value,
                            classId = member.defaultValue.classId,
                        };
                case Vector2Member member:
                    return CreateDefaultVector2Row(nowIso, member.defaultValue);
                case Vector2IntMember member:
                    return CreateDefaultVector2Row(nowIso, member.defaultValue);
                case Vector3Member member:
                    return CreateDefaultVector3Row(nowIso, member.defaultValue);
                case Vector3IntMember member:
                    return CreateDefaultVector3Row(nowIso, member.defaultValue);
                case ColorMember member:
                    return CreateDefaultColorRow(nowIso, member.defaultValue);
                case DecimalMember member:
                    return CreateDefaultDecimalRow(nowIso, member.defaultValue);
                case StringMember member:
                    return member.defaultValue is null
                        ? null
                        : new StringMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = member.defaultValue.value,
                            neoLocalizationMode = member.defaultValue is StringMemberValueBase stringDefault
                                ? stringDefault.neoLocalizationMode
                                : null,
                            classId = member.defaultValue.classId,
                        };
                case EnumMember member:
                    return member.defaultValue is null
                        ? null
                        : new ArrayMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(member.defaultValue.value),
                            classId = member.defaultValue.classId,
                        };
                case LookupMember member:
                    return member.defaultValue is null
                        ? null
                        : new ArrayMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(member.defaultValue.value),
                            classId = member.defaultValue.classId,
                        };
                case DialogueLookupMember member:
                    return member.defaultValue is null
                        ? null
                        : new ArrayMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(member.defaultValue.value),
                            classId = member.defaultValue.classId,
                        };
                case SpriteMember member:
                    return member.defaultValue is null
                        ? null
                        : MemberValueFactory.Create(
                            member,
                            member.defaultValue.value,
                            Guid.NewGuid().ToString(),
                            nowIso,
                            nowIso);
                case AudioMember member:
                    return member.defaultValue is null
                        ? null
                        : MemberValueFactory.Create(
                            member,
                            member.defaultValue.value,
                            Guid.NewGuid().ToString(),
                            nowIso,
                            nowIso);
                case ClassMember member:
                    return CreateDefaultClassValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        classStack);
                case DictionaryMember member:
                    return CreateDefaultDictionaryValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        classStack,
                        env);
                case ListMember member:
                    return CreateDefaultListValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        classStack,
                        env);
                default:
                    return null;
            }
        }

        private static ObjectMemberValue CreateDefaultClassValueRow(
            NeoClient client,
            ClassMember member,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack)
        {
            var effectiveClassId = member.defaultValue?.classId
                ?? member.classId;
            // The slot's constructed arguments travel with every descent
            // below — the default's effective type may be the DECLARED open
            // type, closed only by the slot (specs/class-generics.md
            // §4.1).
            var provided = CloneDefaultClassChildren(
                client,
                member.defaultValue?.value,
                effectiveClassId,
                rows,
                nowIso,
                classStack,
                member.classArguments);
            return CreateWritableClassValueRow(
                client,
                effectiveClassId,
                provided,
                rows,
                nowIso,
                classStack,
                member.classArguments);
        }

        private static Dictionary<string, string> CloneDefaultClassChildren(
            NeoClient client,
            Dictionary<string, string>? source,
            string classId,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null)
        {
            var result = new Dictionary<string, string>();
            if (source is null || source.Count == 0) return result;

            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            foreach (var entry in ResolveMergedSchema(client, classId, classArguments))
            {
                schemaByKey[entry.schemaKey] = entry;
            }
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client,
                classId,
                classArguments);

            foreach (var pair in source)
            {
                if (!schemaByKey.TryGetValue(pair.Key, out var entry)) continue;
                if (!client.TryGetMember(entry.memberId, out Member? member)) continue;
                if (!client.TryGetValue(pair.Value, out MemberValue? sourceRow)) continue;

                var cloned = CloneStoredValueForMember(
                    client,
                    NeoGenericResolution.SubstituteMember(client, member, env),
                    sourceRow,
                    rows,
                    nowIso,
                    classStack,
                    env);
                if (cloned is null) continue;

                rows.Add(cloned);
                result[pair.Key] = cloned.id;
            }
            return result;
        }

        private static ObjectMemberValue? CreateDefaultDictionaryValueRow(
            NeoClient client,
            DictionaryMember member,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (member.defaultValue is null) return null;
            var source = new ObjectMemberValue
            {
                id = "__neo_embedded_dictionary_default",
                value = member.defaultValue.value,
                classId = member.defaultValue.classId,
            };
            return CloneDictionaryValueRow(
                client,
                member,
                source,
                rows,
                nowIso,
                classStack,
                env);
        }

        private static ArrayMemberValue? CreateDefaultListValueRow(
            NeoClient client,
            ListMember member,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (member.defaultValue is null) return null;
            var source = new ArrayMemberValue
            {
                id = "__neo_embedded_list_default",
                value = member.defaultValue.value,
                classId = member.defaultValue.classId,
            };
            return CloneListValueRow(
                client,
                member,
                source,
                rows,
                nowIso,
                classStack,
                env);
        }

        private static MemberValue? CloneStoredValueForMember(
            NeoClient client,
            Member member,
            MemberValue source,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            switch (member)
            {
                case NullMember:
                    return CreateNullValueRow(nowIso, source.classId);
                case BoolMember when source is BoolMemberValue sourceValue:
                    return new BoolMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        classId = source.classId,
                    };
                case IntMember or FloatMember
                    when source is NumberMemberValue sourceValue:
                    return new NumberMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        classId = source.classId,
                    };
                case Vector2Member or Vector2IntMember
                    when source is Vector2MemberValue sourceValue:
                    return new Vector2MemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneVector2(sourceValue.value),
                        classId = source.classId,
                    };
                case Vector3Member or Vector3IntMember
                    when source is Vector3MemberValue sourceValue:
                    return new Vector3MemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneVector3(sourceValue.value),
                        classId = source.classId,
                    };
                case ColorMember when source is ColorMemberValue sourceValue:
                    return new ColorMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneColor(sourceValue.value),
                        classId = source.classId,
                    };
                case DecimalMember when source is StringMemberValue sourceValue:
                    return new StringMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        classId = source.classId,
                    };
                case StringMember when source is StringMemberValue sourceValue:
                    return new StringMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        neoLocalizationMode = sourceValue.neoLocalizationMode,
                        classId = source.classId,
                    };
                case EnumMember or LookupMember or DialogueLookupMember
                    when source is ArrayMemberValue sourceValue:
                    return new ArrayMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneArray(sourceValue.value),
                        classId = source.classId,
                    };
                case SpriteMember when source is SpriteMemberValue sourceValue:
                    return new SpriteMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value is null
                            ? null
                            : new SpriteValue
                            {
                                fileId = sourceValue.value.fileId,
                                sliceIndex = sourceValue.value.sliceIndex,
                            },
                        classId = source.classId,
                    };
                case AudioMember when source is FileMemberValue sourceValue:
                    return new FileMemberValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value is null
                            ? null
                            : new FileValue { fileId = sourceValue.value.fileId },
                        classId = source.classId,
                    };
                case ClassMember classMember
                    when source is ObjectMemberValue sourceValue:
                    return CreateWritableClassValueRow(
                        client,
                        sourceValue.classId ?? classMember.classId,
                        CloneDefaultClassChildren(
                            client,
                            sourceValue.value,
                            sourceValue.classId ?? classMember.classId,
                            rows,
                            nowIso,
                            classStack,
                            classMember.classArguments),
                        rows,
                        nowIso,
                        classStack,
                        classMember.classArguments);
                case DictionaryMember dictionaryMember
                    when source is ObjectMemberValue sourceValue:
                    return CloneDictionaryValueRow(
                        client,
                        dictionaryMember,
                        sourceValue,
                        rows,
                        nowIso,
                        classStack,
                        env);
                case ListMember listMember
                    when source is ArrayMemberValue sourceValue:
                    return CloneListValueRow(
                        client,
                        listMember,
                        sourceValue,
                        rows,
                        nowIso,
                        classStack,
                        env);
                default:
                    return null;
            }
        }

        private static ObjectMemberValue CloneDictionaryValueRow(
            NeoClient client,
            DictionaryMember member,
            ObjectMemberValue source,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            // The clone keeps the source row's immutable Decision-9 stamp
            // (falling back to a fresh computation from the creation env
            // for pre-stamp authored rows), and entries substitute their
            // member through it.
            var entryEnv = source.genericBindings is null
                ? env
                : NeoGenericResolution.EnvFromStamp(source.genericBindings);
            var value = new Dictionary<string, string>();
            if (source.value is not null)
            {
                if (!client.TryGetMember(
                        member.entryMemberId,
                        out Member? entryMember))
                {
                    throw new InvalidOperationException(
                        $"Dictionary default for '{member.name}' references missing entry member '{member.entryMemberId}'.");
                }
                entryMember = NeoGenericResolution.SubstituteMember(client, entryMember, entryEnv);
                foreach (var pair in source.value)
                {
                    if (!client.TryGetValue(pair.Value, out MemberValue? sourceRow))
                    {
                        throw new InvalidOperationException(
                            $"Dictionary default for '{member.name}' key '{pair.Key}' references missing value '{pair.Value}'.");
                    }
                    var cloned = CloneStoredValueForMember(
                        client,
                        entryMember,
                        sourceRow,
                        rows,
                        nowIso,
                        classStack,
                        entryEnv);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary default for '{member.name}' key '{pair.Key}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    value[pair.Key] = cloned.id;
                }
            }

            var row = new ObjectMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value,
                classId = source.classId,
                genericBindings = source.genericBindings is null
                    ? null
                    : new Dictionary<string, string>(source.genericBindings),
            };
            NeoGenericResolution.StampGenericBindings(client, member, row, env);
            return row;
        }

        private static ArrayMemberValue CloneListValueRow(
            NeoClient client,
            ListMember member,
            ArrayMemberValue source,
            List<MemberValue> rows,
            string nowIso,
            HashSet<string> classStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            // Same stamp semantics as CloneDictionaryValueRow.
            var entryEnv = source.genericBindings is null
                ? env
                : NeoGenericResolution.EnvFromStamp(source.genericBindings);
            var value = new List<string>();
            if (source.value is not null)
            {
                if (!client.TryGetMember(
                        member.entryMemberId,
                        out Member? entryMember))
                {
                    throw new InvalidOperationException(
                        $"List default for '{member.name}' references missing entry member '{member.entryMemberId}'.");
                }
                entryMember = NeoGenericResolution.SubstituteMember(client, entryMember, entryEnv);
                foreach (var sourceId in source.value)
                {
                    if (!client.TryGetValue(sourceId, out MemberValue? sourceRow))
                    {
                        throw new InvalidOperationException(
                            $"List default for '{member.name}' references missing value '{sourceId}'.");
                    }
                    var cloned = CloneStoredValueForMember(
                        client,
                        entryMember,
                        sourceRow,
                        rows,
                        nowIso,
                        classStack,
                        entryEnv);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"List default for '{member.name}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    value.Add(cloned.id);
                }
            }

            var row = new ArrayMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value.ToArray(),
                classId = source.classId,
                genericBindings = source.genericBindings is null
                    ? null
                    : new Dictionary<string, string>(source.genericBindings),
            };
            NeoGenericResolution.StampGenericBindings(client, member, row, env);
            return row;
        }

        private static NullMemberValue CreateNullValueRow(
            string nowIso,
            string? classId)
        {
            return new NullMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                classId = classId,
            };
        }

        private static Vector2MemberValue? CreateDefaultVector2Row(
            string nowIso,
            MemberValueBase<NeoVector2Value?>? defaultValue)
        {
            return defaultValue is null
                ? null
                : new Vector2MemberValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = CloneVector2(defaultValue.value),
                    classId = defaultValue.classId,
                };
        }

        private static Vector3MemberValue? CreateDefaultVector3Row(
            string nowIso,
            MemberValueBase<NeoVector3Value?>? defaultValue)
        {
            return defaultValue is null
                ? null
                : new Vector3MemberValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = CloneVector3(defaultValue.value),
                    classId = defaultValue.classId,
                };
        }

        /// <summary>
        /// Default-value row for a Color member. Unlike the vectors,
        /// Color has a well-defined identity default — opaque white
        /// (specs/color-member.md decision 4) — so an absent authored
        /// default still materializes a row rather than leaving a required
        /// field valueless.
        /// </summary>
        private static ColorMemberValue CreateDefaultColorRow(
            string nowIso,
            MemberValueBase<NeoColorValue?>? defaultValue)
        {
            return new ColorMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = CloneColor(defaultValue?.value)
                    ?? new NeoColorValue { r = 1f, g = 1f, b = 1f, a = 1f },
                classId = defaultValue?.classId,
            };
        }

        private static string[]? CloneArray(string[]? source)
        {
            if (source is null) return null;
            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static NeoVector2Value? CloneVector2(NeoVector2Value? source)
        {
            return source is null ? null : new NeoVector2Value { x = source.x, y = source.y };
        }

        private static NeoVector3Value? CloneVector3(NeoVector3Value? source)
        {
            return source is null
                ? null
                : new NeoVector3Value { x = source.x, y = source.y, z = source.z };
        }

        private static NeoColorValue? CloneColor(NeoColorValue? source)
        {
            return source is null
                ? null
                : new NeoColorValue { r = source.r, g = source.g, b = source.b, a = source.a };
        }

        /// <summary>
        /// Default-value row for a Decimal member. Decimal has a
        /// well-defined non-null default — canonical "0"
        /// (specs/decimal-member.md decision 4) — so an absent authored
        /// default still materializes a row (a string row, decision 5) rather
        /// than leaving a required field valueless.
        /// </summary>
        private static StringMemberValue CreateDefaultDecimalRow(
            string nowIso,
            MemberValueBase<string?>? defaultValue)
        {
            return new StringMemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = defaultValue?.value ?? "0",
            };
        }

        public static NeoValuePayload? ValuePayload(
            INeoValuePayloadProvider? value)
        {
            return value?.ToNeoValuePayload();
        }

        public static NeoValuePayload ValuePayload(
            NeoMemberClass node,
            string fallbackClassId)
        {
            return new NeoValuePayload(
                node.value?.value,
                node.value?.classId ?? fallbackClassId);
        }

        public static int? ReadInt(NeoMemberInt member)
        {
            var value = member.value?.value;
            return value.HasValue ? (int)value.Value : null;
        }

        public static string? ReadSingleSelected(NeoMemberEnum member)
        {
            var selected = member.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static string? ReadSingleSelected(NeoMemberLookup member)
        {
            var selected = member.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static string? ReadSingleSelected(NeoMemberDialogueLookup member)
        {
            var selected = member.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static TEnum? ReadEnumSingle<TEnum>(
            string[] optionIds,
            Func<string, TEnum> create)
        {
            return optionIds.Length == 0 ? default : create(optionIds[0]);
        }

        public static IReadOnlyList<TEnum> ReadEnumList<TEnum>(
            string[] optionIds,
            Func<string, TEnum> create)
        {
            var values = new List<TEnum>();
            foreach (var optionId in optionIds) values.Add(create(optionId));
            return values;
        }

        public static IReadOnlyList<T> ReadLookupList<T>(
            IList<NeoMember> nodes,
            Func<NeoMember, T> create)
        {
            var values = new List<T>();
            foreach (var node in nodes) values.Add(create(node));
            return values;
        }

        public static object? ReadNSProperty(NeoMemberNSProperty member)
        {
            var result = member.Compute();
            if (!result.ok)
            {
                throw new InvalidOperationException(
                    result.error ?? "NSProperty getter evaluation failed.");
            }
            return result.value;
        }

        public static Sprite? ReadSprite(NeoClient client, object? value)
        {
            return NeoAssetResolver.ResolveSprite(
                client.assetDatabase,
                ToSpriteValue(value));
        }

        public static AudioClip? ReadAudioClip(NeoClient client, object? value)
        {
            return NeoAssetResolver.ResolveAudioClip(
                client.assetDatabase,
                ToFileValue(value));
        }

        private static SpriteValue? ToSpriteValue(object? value)
        {
            if (value is null) return null;
            if (value is SpriteValue spriteValue) return spriteValue;
            if (value is JObject obj)
            {
                var fileId = obj["fileId"]?.Value<string>();
                var sliceIndex = obj["sliceIndex"]?.Value<int?>();
                return string.IsNullOrWhiteSpace(fileId) || sliceIndex == null
                    ? null
                    : new SpriteValue { fileId = fileId!, sliceIndex = sliceIndex.Value };
            }
            if (value is IDictionary<string, object?> dict &&
                dict.TryGetValue("fileId", out var rawFileId) &&
                rawFileId is string dictFileId &&
                dict.TryGetValue("sliceIndex", out var rawSliceIndex))
            {
                return rawSliceIndex switch
                {
                    int i => new SpriteValue { fileId = dictFileId, sliceIndex = i },
                    long l => new SpriteValue { fileId = dictFileId, sliceIndex = (int)l },
                    double d => new SpriteValue { fileId = dictFileId, sliceIndex = Convert.ToInt32(d) },
                    _ => null,
                };
            }
            return null;
        }

        private static FileValue? ToFileValue(object? value)
        {
            if (value is null) return null;
            if (value is FileValue fileValue) return fileValue;
            if (value is JObject obj)
            {
                var fileId = obj["fileId"]?.Value<string>();
                return string.IsNullOrWhiteSpace(fileId)
                    ? null
                    : new FileValue { fileId = fileId! };
            }
            if (value is IDictionary<string, object?> dict &&
                dict.TryGetValue("fileId", out var rawFileId) &&
                rawFileId is string dictFileId)
            {
                return new FileValue { fileId = dictFileId };
            }
            return null;
        }

        public static T? ReadNSPropertyClass<T>(
            NeoClient client,
            object? value,
            bool required,
            bool saved,
            Func<NeoClient, NeoMemberClass, T>? readOnlyFactory,
            // Nullable: an Immutable-constrained type (allowedStorage collapse)
            // generates no writable class, so codegen passes null here.
            Func<NeoClient, NeoMemberClassWritable, T>? savedFactory)
        {
            if (value is null)
            {
                if (required)
                {
                    throw new InvalidOperationException(
                        "NSProperty getter returned null for a required class value.");
                }
                return default;
            }

            if (value is T typed) return typed;

            string? valueId = ValueId(value);
            if (string.IsNullOrEmpty(valueId))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned a class value without a backing value id. Runtime value type: {value.GetType().FullName}.");
            }

            if (!client.TryGetValue(valueId, out MemberValue? untypedRow))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned class value id '{valueId}', but no backing value row exists. Runtime value type: {value.GetType().FullName}.");
            }

            if (untypedRow is not ObjectMemberValue row)
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned class value id '{valueId}', but the backing row is not an object value. Row type: {untypedRow.GetType().FullName}.");
            }
            string? classId = ResolveClassValueClassId(client, valueId!, row);
            if (string.IsNullOrEmpty(classId))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned class value id '{valueId}', but the backing row does not declare a classId and its owning member could not be inferred.");
            }

            var member = new ClassMember
            {
                id = $"__neo_nsg_class_{classId}",
                name = "NSPropertyClassValue",
                kind = MemberKind.Class,
                classId = classId,
                createdAt = row.createdAt,
                updatedAt = row.updatedAt,
            };

            if (saved)
            {
                // Stable-id overlay: a value reachable from the save/session
                // root reports that ownership directly (see the authored
                // ownership map); the returned writable node clone-on-writes
                // its own row at its stable id on first mutation, so there is
                // no path to pre-materialize here.
                if (!client.TryGetValueOwnership(valueId, out NeoValueOwnership ownership)
                    || (ownership != NeoValueOwnership.Save && ownership != NeoValueOwnership.Session))
                {
                    throw new InvalidOperationException(
                        "NSProperty getter returned an asset-owned class value where a saved value was expected.");
                }

                if (savedFactory is null)
                {
                    throw new InvalidOperationException(
                        "NSProperty getter class value resolved to a writable placement, but the class's allowedStorage is immutable (no writable factory exists).");
                }
                return savedFactory(
                    client,
                    new NeoMemberClassWritable(client, member, valueId, ownership));
            }

            if (readOnlyFactory is null)
            {
                throw new InvalidOperationException(
                    "NSProperty getter class value requires a read-only factory.");
            }

            return readOnlyFactory(
                client,
                new NeoMemberClass(client, member, valueId));
        }

        public static T ReadRequiredNSPropertyClass<T>(
            NeoClient client,
            object? value,
            bool saved,
            Func<NeoClient, NeoMemberClass, T>? readOnlyFactory,
            Func<NeoClient, NeoMemberClassWritable, T>? savedFactory)
        {
            T? resolved = ReadNSPropertyClass(
                client,
                value,
                true,
                saved,
                readOnlyFactory,
                savedFactory);
            if (resolved is null)
            {
                throw new InvalidOperationException(
                    "NSProperty getter returned null for a required class value.");
            }
            return resolved;
        }

        public static string? ValueId(object? value)
        {
            if (value is NeoLookupSelection selection) return selection.valueId;
            if (value is NeoDialogueReference dialogueReference) return dialogueReference.Id;
            return value is INeoValueReference reference
                ? reference.valueId
                : null;
        }

        public static string[] ToStringArray(object? value)
        {
            if (value is null) return Array.Empty<string>();
            if (value is string[] strings) return strings;
            if (value is object?[] objects)
            {
                var values = new List<string>();
                foreach (var item in objects)
                {
                    if (item is string str) values.Add(str);
                }
                return values.ToArray();
            }
            return Array.Empty<string>();
        }

        public static string[] LookupSelectionIds(
            IEnumerable<NeoLookupSelection>? selections)
        {
            if (selections is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var selection in selections) ids.Add(selection.valueId);
            return ids.ToArray();
        }

        public static string LookupSelectionId(string? valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
            {
                throw new InvalidOperationException(
                    "Generated value is not bound to a lookup-selectable value id.");
            }
            return valueId;
        }

        /// <summary>
        /// Flattens a set of <see cref="NeoDialogueReference"/>s to their stored
        /// <c>dialogueId</c>s for serialization (multiselect DialogueLookup).
        /// </summary>
        public static string[] DialogueReferenceIds(
            IEnumerable<NeoDialogueReference>? references)
        {
            if (references is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var reference in references) ids.Add(reference.Id);
            return ids.ToArray();
        }

        /// <summary>
        /// Creates the generated wrapper for a class-default asset without
        /// inventing a definition value id. Used by grid bindings and editor
        /// asset synchronization for schema-9 class-backed world assets.
        /// </summary>
        public static NeoGeneratedClassValue CreateReadOnlyClassDefault(
            NeoClient client,
            string classId,
            IReadOnlyDictionary<string, ReadOnlyClassFactory> readOnlyFactories)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException("Class id cannot be empty.", nameof(classId));
            }
            if (readOnlyFactories == null)
            {
                throw new ArgumentNullException(nameof(readOnlyFactories));
            }
            if (!readOnlyFactories.TryGetValue(classId, out var factory))
            {
                throw new InvalidOperationException(
                    $"No generated read-only factory exists for class '{classId}'. Regenerate the project's C# types.");
            }

            var now = NeoTimestamp.Now();
            var member = new ClassMember
            {
                id = $"__neo_class_default:{classId}",
                name = "ClassDefault",
                kind = MemberKind.Class,
                classId = classId,
                defaultValue = new ObjectMemberValueBase
                {
                    classId = classId,
                    value = new Dictionary<string, string>(),
                },
                createdAt = now,
                updatedAt = now,
            };
            object value = factory(client, new NeoMemberClass(client, member, null));
            if (value is not NeoGeneratedClassValue generated)
            {
                throw new InvalidOperationException(
                    $"Generated factory for class '{classId}' did not return a NeoGeneratedClassValue.");
            }
            generated.MarkClassDefaultReference();
            return generated;
        }
    }
}
