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
    /// P43 §8 — one argument of a generated C# constructor, matched to a
    /// declared NeoScript parameter <b>by name</b>. Named rather than
    /// positional because NeoScript overload resolution is name-first
    /// (§6.1.1) and because a generated signature must stay readable when a
    /// later overload reorders parameters.
    /// </summary>
    public sealed class NeoDeclaredConstructorArgument
    {
        public string name { get; }
        public object? value { get; }

        public NeoDeclaredConstructorArgument(string name, object? value)
        {
            this.name = name
                ?? throw new ArgumentNullException(nameof(name));
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
        /// <summary>
        /// Returns the persisted Neo binding carried by a typed delegate from
        /// a generated member getter. Used by generated static setters when a
        /// value row must be materialized before the assignment is applied.
        /// </summary>
        public static NeoDelegateValue? DelegateValue(Delegate? value) =>
            NeoMemberDelegate.PersistedBindingOf(value);

        /// <summary>
        /// Resolves the listener a C# <c>+=</c> supplied to the member target
        /// NeoScript's <c>+=</c> lowers to (P62 §5.2), so identity,
        /// deduplication and removal are byte-identical across the two
        /// languages.
        ///
        /// <para>A method group over a generated member carries exactly the
        /// two facts a target needs: the delegate's
        /// <see cref="Delegate.Target"/> is the generated instance, which
        /// knows its own <c>valueId</c> (a static member method has none, and
        /// maps to null), and its <see cref="Delegate.Method"/> is stamped
        /// with <see cref="NeoMemberMethodAttribute"/>. A delegate previously
        /// read from an <c>NSDelegate</c> member resolves through the
        /// persisted-bindings table instead. Anything else — a lambda, a
        /// native method group, a combined multicast delegate — throws here,
        /// at the subscribing line, rather than silently at invoke.</para>
        /// </summary>
        /// <param name="ownerValueId">
        /// The row that owns the action being subscribed, when the caller
        /// knows it. A target resolving to that row is stored with a null
        /// <c>valueId</c>, the identity NeoScript mints for the same
        /// subscription; pass null to keep the resolved row id verbatim.
        /// </param>
        public static NeoDelegateValue ListenerTargetOf(
            Delegate listener,
            string? ownerValueId = null)
        {
            if (listener is null)
            {
                throw new NeoActionListenerException(
                    "Cannot subscribe a null listener to an NSAction member. A listener must be a generated Neo member method or a Neo-obtained delegate.");
            }
            if (listener.GetInvocationList().Length != 1)
            {
                throw new NeoActionListenerException(
                    "Cannot subscribe a combined multicast delegate to an NSAction member; subscribe each Neo member method on its own so every listener keeps a removable identity.");
            }
            var stamp = (NeoMemberMethodAttribute?)Attribute.GetCustomAttribute( // neo-terminology-audit: allow-line legacy-attribute-domain-word -- System.Attribute reflection over the C# [NeoMemberMethod] attribute, not a Neo domain concept
                listener.Method,
                typeof(NeoMemberMethodAttribute));
            if (stamp is not null)
            {
                return OwnerCanonicalListener(
                    new NeoDelegateValue
                    {
                        memberId = stamp.memberId,
                        valueId = ListenerValueIdOf(listener, stamp.memberId),
                    },
                    ownerValueId);
            }
            NeoDelegateValue? persisted = PersistedListenerBindingOf(listener);
            if (persisted is not null && persisted.IsMemberTarget)
            {
                return OwnerCanonicalListener(persisted, ownerValueId);
            }
            if (persisted is not null)
            {
                throw new NeoActionListenerException(
                    $"Delegate '{listener.Method.Name}' was read from an NSDelegate member holding a closure, which has no identity to deduplicate or remove by. Subscribe the NSDelegate member itself so the action indirects through it.");
            }
            throw new NeoActionListenerException(
                $"Delegate '{listener.Method.Name}' is not a Neo listener. A listener must be a generated Function/NSFunction method group or a delegate obtained from Neo data — a native lambda has no data representation and cannot be persisted as a subscription.");
        }

        /// <summary>
        /// Identity check behind a generated action property's setter
        /// (P62 §5.2). <c>obj.OnX += h</c> compiles to get →
        /// <c>operator+</c> → set, and the operator returns the same
        /// instance, so the only legal assignment is the member's own live
        /// action; every other one is a reassignment of a listener set that
        /// is subscribed to, never replaced.
        /// </summary>
        public static void RequireSameAction(
            object? value,
            NeoActionBase expected,
            string memberLabel)
        {
            if (expected is null)
            {
                throw new ArgumentNullException(
                    nameof(expected),
                    $"'{memberLabel}' has no bound action to compare against; the generated getter always returns one.");
            }
            if (value is null)
            {
                throw new NeoActionReassignmentException(
                    $"'{memberLabel}' cannot be assigned null. An NSAction member's rest state is an empty listener set, so subscribe with += and unsubscribe with -=.");
            }
            if (!ReferenceEquals(value, expected))
            {
                // Reference identity is the whole check (§5.2). `obj.OnX += h`
                // compiles to get → operator+ → set and the operator returns
                // the very instance the getter handed out, so anything else —
                // another row's action, another member's action, a stale
                // instance — is a reassignment of a listener set that is
                // subscribed to, never replaced. `memberLabel` is error text
                // only; comparing names would reject a member whose display
                // name and schema key differ, which is ordinary.
                throw new NeoActionReassignmentException(
                    $"'{memberLabel}' was assigned an action other than its own. Actions subscribe with += and -=; they are never reassigned from one member or row to another.");
            }
        }

        private static string? ListenerValueIdOf(Delegate listener, string memberId)
        {
            // A static generated member method has no receiver, which is the
            // declaration-default target: valueId null.
            if (listener.Target is null) return null;
            if (listener.Target is NeoGeneratedClassValue generated)
            {
                return generated.valueId;
            }
            throw new NeoActionListenerException(
                $"Method group for Neo member '{memberId}' is bound to a '{listener.Target.GetType().Name}' receiver rather than a generated Neo value, so it has no valueId to subscribe with.");
        }

        /// <summary>
        /// Canonicalizes a listener identity against the row that owns the
        /// action being subscribed (P62 §5.2). A target resolving to that
        /// very row is spelled with a null <c>valueId</c> — byte-identical to
        /// what NeoScript's <c>this.OnX += this.Handler</c> and an authored
        /// <c>= [Handler]</c> default lower to — so identity, deduplication
        /// and removal are interchangeable across the two languages. A
        /// genuinely foreign row keeps its id.
        /// </summary>
        private static NeoDelegateValue OwnerCanonicalListener(
            NeoDelegateValue target,
            string? ownerValueId)
        {
            if (target.valueId is null) return target;
            if (ownerValueId is null) return target;
            if (!string.Equals(target.valueId, ownerValueId, StringComparison.Ordinal))
            {
                return target;
            }
            return new NeoDelegateValue
            {
                memberId = target.memberId,
                valueId = null,
            };
        }

        /// <summary>
        /// Probes the delegate persisted-bindings table without adopting its
        /// throw: an unseen method group is the common case here (it resolves
        /// through the attribute path), and the failure this helper's callers <!-- neo-terminology-audit: allow-line legacy-attribute-domain-word -- "attribute path" is dispatch via the C# [NeoMemberMethod] attribute, not a Neo domain concept -->
        /// must raise is <see cref="NeoActionListenerException"/>, not the
        /// delegate setter's serialization error.
        /// </summary>
        private static NeoDelegateValue? PersistedListenerBindingOf(Delegate listener)
        {
            try
            {
                return NeoMemberDelegate.PersistedBindingOf(listener);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

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

        private sealed class ConstructorSchemaCache
        {
            internal readonly object gate = new();
            internal readonly Dictionary<string, IList<MergedSchemaEntry>>
                mergedSchemas = new();
            internal readonly Dictionary<
                string,
                IReadOnlyDictionary<string, NeoGenericEnvEntry>>
                genericEnvironments = new();
            internal readonly Dictionary<
                ConstructorMetadataCacheKey,
                RuntimeConstructorMetadata> emptyFieldMetadata = new();
            internal readonly Dictionary<string, RuntimeClassPlan>
                classPlans = new();
        }

        internal sealed class RuntimeClassPlan
        {
            internal IList<MergedSchemaEntry> schema = null!;
            internal IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                null!;
            internal Dictionary<string, MergedSchemaEntry> schemaByKey = null!;
            internal Dictionary<string, Member> membersBySchemaKey = null!;
            internal ClassMember factoryMember = null!;
        }

        private readonly struct ConstructorMetadataCacheKey :
            IEquatable<ConstructorMetadataCacheKey>
        {
            internal ConstructorMetadataCacheKey(
                ClassTypeInfo classTypeInfo,
                bool requireSuppliedRequiredFields)
            {
                classId = classTypeInfo.classId;
                type = classTypeInfo.type;
                required = classTypeInfo.required;
                this.requireSuppliedRequiredFields =
                    requireSuppliedRequiredFields;
            }

            private readonly string classId;
            private readonly MemberKind type;
            private readonly bool required;
            private readonly bool requireSuppliedRequiredFields;

            public bool Equals(ConstructorMetadataCacheKey other) =>
                classId == other.classId
                && type == other.type
                && required == other.required
                && requireSuppliedRequiredFields
                    == other.requireSuppliedRequiredFields;

            public override bool Equals(object? obj) =>
                obj is ConstructorMetadataCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = classId?.GetHashCode() ?? 0;
                    hash = (hash * 397) ^ (int)type;
                    hash = (hash * 397) ^ required.GetHashCode();
                    return (hash * 397)
                        ^ requireSuppliedRequiredFields.GetHashCode();
                }
            }
        }

        // NeoClient already treats schema inheritance and generic binding
        // resolution as stable after their first per-client lookup. Constructor-
        // heavy animation data commonly creates the same handful of runtime
        // classes hundreds of times, so extend that cache boundary to the
        // constructor-specific merged plan. Weak keys retain no disposed client.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            NeoClient,
            ConstructorSchemaCache> ConstructorSchemaCaches = new();

        internal sealed class RuntimeConstructorField
        {
            internal string schemaKey = null!;
            internal string memberId = null!;
            internal object? value;
        }

        internal sealed class RuntimeConstructorMetadata
        {
            internal Dictionary<string, Member> membersBySchemaKey = null!;
            internal IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv = null!;
            internal RuntimeClassPlan classPlan = null!;
        }

        internal readonly struct RuntimeConstructedClassValue
        {
            internal RuntimeConstructedClassValue(
                ObjectMemberValue value,
                ClassMember member)
            {
                this.value = value;
                this.member = member;
            }

            internal ObjectMemberValue value { get; }
            internal ClassMember member { get; }
        }

        /// <summary>
        /// P43 §7.2.3 — maximum nesting of class construction. Matches the
        /// NSFunction runtime's <c>MaxCallableDepth</c> so a graph that
        /// compiles also constructs, and is counted on its own stack because
        /// construction recurses through member initializers and base
        /// constructors rather than through NSFunction calls.
        /// </summary>
        internal const int MaxConstructionDepth = 64;

        /// <summary>
        /// P43 §7.2.3 — opens one construction frame on
        /// <paramref name="ctx"/>, enforcing the depth cap BEFORE anything is
        /// materialized. Mirrors <c>pushConstructionFrame</c> in
        /// evaluateNSGetter.ts one for one, including the label vocabulary
        /// (a class name for a construction, <c>"&lt;member&gt; initializer"</c>
        /// for a computed default), so the two runtimes trip at the same
        /// nesting on the same graph and print the same chain.
        /// </summary>
        internal static NeoScript.NSGetterEvaluator.Context PushConstructionFrame(
            NeoScript.NSGetterEvaluator.Context ctx,
            string label)
        {
            ctx.allocationTracker.ConsumeWorkUnit();
            if (ctx.constructionStack.Count >= MaxConstructionDepth)
            {
                var chain = new List<string>(ctx.constructionStack) { label };
                // Worded exactly like the TypeScript evaluator's cap: the
                // shared P43 parity fixture asserts this text on both runtimes,
                // so a divergence here is a divergence in the gate itself.
                throw new NeoScript.NSGetterRuntimeError(
                    $"Class construction depth exceeded {MaxConstructionDepth} frames: {string.Join(" -> ", chain)}.");
            }
            return ctx.WithConstructionPushed(label);
        }

        /// <summary>
        /// Per-construction state shared by the whole default-materialization
        /// recursion. It replaces the bare <c>classStack</c> that used to be
        /// threaded through those methods, because P43 gives them two more
        /// things to carry: the evaluation context an <c>init</c>-backed
        /// default is evaluated on, and the reference-ownership map an
        /// initializer's product is attached through.
        /// </summary>
        internal sealed class NeoConstructionScope
        {
            private readonly NeoClient client;
            private readonly IReadOnlyList<object?> initializerArguments;
            private NeoScript.NSGetterEvaluator.Context? evaluationContext;

            internal NeoConstructionScope(
                NeoClient client,
                NeoScript.NSGetterEvaluator.Context? evaluationContext,
                IReadOnlyList<object?>? initializerArguments = null)
            {
                this.client = client;
                this.evaluationContext = evaluationContext;
                this.initializerArguments = initializerArguments
                    ?? Array.Empty<object?>();
            }

            /// <summary>
            /// Per-class recursion guard for literal defaults. Unchanged
            /// behavior: a class whose literal default graph contains itself
            /// is rejected by name.
            /// </summary>
            internal HashSet<string> classStack { get; } = new HashSet<string>();

            /// <summary>
            /// Ownership of every already-owned value an initializer or a
            /// supplied field attached, keyed by dotted path.
            /// <see cref="PrepareConstructedGraph"/> preflights and imports
            /// these after the staged graph passes shape validation.
            /// </summary>
            internal Dictionary<string, NeoValueOwnership>
                referenceOwnershipByPath { get; } =
                    new Dictionary<string, NeoValueOwnership>();

            internal NeoScript.NSGetterEvaluator.Context? ExistingEvaluationContext =>
                evaluationContext;

            /// <summary>
            /// The context initializer bodies evaluate on. When construction
            /// starts from generated C# rather than from inside a NeoScript
            /// frame there is no ambient context, so a minimal one is built
            /// from the client with <c>root</c> resolved exactly as an
            /// NSFunction invocation resolves it.
            /// </summary>
            internal NeoScript.NSGetterEvaluator.Context EvaluationContext
            {
                get
                {
                    if (evaluationContext is null)
                    {
                        var seed = new NeoScript.NSGetterEvaluator.Context(
                            client,
                            null,
                            null,
                            valueOwnership: NeoValueOwnership.Session);
                        evaluationContext = seed.WithRoot(
                            NeoScriptValueMarshaller.ResolveRoot(client, seed));
                    }
                    return evaluationContext;
                }
            }

            /// <summary>
            /// Resolves an initializer's product back to the row it created so
            /// it is attached through the ordinary constructor-reference funnel
            /// rather than copied field by field.
            /// </summary>
            internal Func<object?, NeoConstructorValueReference?> ValueReference =>
                value => NeoScript.NSGetterEvaluator.ConstructorReferenceOf(
                    value,
                    EvaluationContext);

            internal bool IsAllocatedSessionRoot(string valueId) =>
                EvaluationContext.allocationTracker.IsAllocatedSessionRoot(valueId);

            /// <summary>
            /// P43 §1.1 — runs a computed default. <c>__this__</c> is null: an
            /// initializer produces the member's value and has no instance to
            /// read, exactly as the TS evaluator binds it.
            ///
            /// <para>The body runs on its own construction frame (§7.2.3), the
            /// way <c>evaluateInitializerInContext</c> does on the TS side: an
            /// initializer that constructs is a construction step, and without
            /// its frame the two runtimes would cap at different nesting on the
            /// same graph.</para>
            /// </summary>
            internal object? EvaluateInitializer(
                Member member,
                InitializerBody init)
            {
                if (init.compiled is null)
                {
                    throw new InvalidOperationException(
                        $"Initializer for '{member.name}' has no compiled body. Re-export the project from the current web app.");
                }
                NeoScript.NSGetterEvaluator.Context initializerContext =
                    PushConstructionFrame(
                        EvaluationContext,
                        $"{member.name} initializer");
                IReadOnlyList<object?> arguments =
                    init.compiled.parameters is { Length: > 2 }
                        ? initializerArguments
                        : Array.Empty<object?>();
                return NeoScript.NSGetterEvaluator.Evaluate(
                    init.compiled,
                    initializerContext.thisValue is null
                        ? initializerContext
                        : initializerContext.WithThis(null),
                    arguments);
            }
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
        /// P67 §7.1 — resolves one declared variant of <typeparamref name="T"/>.
        /// Generated `Class.Variants` entries call this helper.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoVariant<T> ResolveVariant<T>(
            NeoClient client,
            string variantId)
            where T : NeoGeneratedClassValue
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(variantId))
            {
                throw new ArgumentException(
                    "A variant id is required.",
                    nameof(variantId));
            }
            return client.GetOrCreateVariant<T>(variantId);
        }

        /// <summary>P68 §7 — resolves one collection-bound variant handle.</summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoLookupVariant<T, TValue> ResolveLookupVariant<T, TValue>(
            NeoClient client,
            string variantId)
            where T : NeoGeneratedClassValue
            where TValue : NeoGeneratedClassValue
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(variantId))
            {
                throw new ArgumentException(
                    "A lookup variant id is required.",
                    nameof(variantId));
            }
            return client.GetOrCreateLookupVariant<T, TValue>(variantId);
        }

        /// <summary>
        /// P67 §3.4 — resolves the reserved `Base` entry: the class itself with
        /// no variant applied, whose `Initialize` is the class's own
        /// construction. Generated `Class.Variants.Base` calls this helper.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoVariant<T> ResolveBaseVariant<T>(
            NeoClient client,
            string classId)
            where T : NeoGeneratedClassValue
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException(
                    "A class id is required.",
                    nameof(classId));
            }
            return client.GetOrCreateBaseVariant<T>(classId);
        }

        /// <summary>
        /// P67 §7.4 — reads a `Variant` member's stored `{classId, variantId}`
        /// pair into a resolved handle.
        ///
        /// <para>A null <c>variantId</c> resolves to the base entry of the
        /// *stored* class, which may be a subclass of the member's declared
        /// target: §6 covariance is a property of the value, so the read has to
        /// honour it rather than re-deriving the class from `T`.</para>
        ///
        /// <para>Returns null only when the member holds no selection at all,
        /// which a nullable declaration is the only way to reach.</para>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoVariant<T>? ResolveVariantValue<T>(NeoMemberVariant node)
            where T : NeoGeneratedClassValue
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            VariantRefValue? selection = node.value?.value;
            if (selection is null)
            {
                if (!node.member.required) return null;
                throw new InvalidOperationException(
                    $"Required variant member '{node.member.name}' has no selection.");
            }
            if (selection.variantId is null)
            {
                if (selection.rowValueId is not null)
                {
                    throw new InvalidOperationException(
                        $"Variant member '{node.member.name}' binds row '{selection.rowValueId}' to Base.");
                }
                return node.Client.GetOrCreateBaseVariant<T>(selection.classId);
            }
            if (selection.rowValueId is not null)
            {
                if (!node.Client.TryGetVariant(
                        selection.variantId,
                        out VariantRecord? record))
                {
                    throw new InvalidOperationException(
                        $"Variant '{selection.variantId}' is not in this project export.");
                }
                NeoGeneratedClassValue? row =
                    node.Client.ResolveRegisteredGeneratedClassValue(selection.rowValueId);
                if (row is null)
                {
                    throw new InvalidOperationException(
                        $"Variant member '{node.member.name}' binds missing or unregistered row '{selection.rowValueId}'. Regenerate the project's C# types.");
                }
                NeoVariantSupport.ValidateLookupRow(node.Client, record!, row);
                return new NeoVariant<T>(node.Client, selection.classId, record, row);
            }
            return node.Client.GetOrCreateVariant<T>(selection.variantId);
        }

        /// <summary>P68 §6 — resolves an unbound lookup-variant member.</summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoLookupVariant<T, TValue>? ResolveLookupVariantValue<T, TValue>(
            NeoMemberVariant node)
            where T : NeoGeneratedClassValue
            where TValue : NeoGeneratedClassValue
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            VariantRefValue? selection = node.value?.value;
            if (selection is null)
            {
                if (!node.member.required) return null;
                throw new InvalidOperationException(
                    $"Required lookup variant member '{node.member.name}' has no selection.");
            }
            if (selection.variantId is null)
            {
                throw new InvalidOperationException(
                    $"Lookup variant member '{node.member.name}' cannot select Base.");
            }
            if (selection.rowValueId is not null)
            {
                throw new InvalidOperationException(
                    $"Lookup variant member '{node.member.name}' must stay unbound; row '{selection.rowValueId}' belongs on a plain Variant member.");
            }
            return node.Client.GetOrCreateLookupVariant<T, TValue>(selection.variantId);
        }

        /// <summary>
        /// P67 §7.4 — the write half. A variant member is written by identity:
        /// the handle already knows which class and which record it names, and
        /// nothing about the variant's own graph is copied into the member.
        ///
        /// <para>Known limitation: a handle minted by a DIFFERENT client is
        /// accepted without validation. What crosses is a pair of ids, not a
        /// live object, so the write is well defined; it is only meaningful if
        /// the destination project happens to carry the same ids. Validating
        /// here is not possible — the destination is the node
        /// <see cref="SetValue"/> receives, which this marshaller never sees —
        /// and commit-time validation is the authority on dangling variant ids
        /// (P67 §6), so a bad pair is caught on the next push rather than
        /// silently resolving to the wrong variant at runtime: a missing record
        /// throws on resolution.</para>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoValueWritePayload? VariantValue<T>(NeoVariant<T>? variant)
            where T : NeoGeneratedClassValue
        {
            if (variant is null) return Value<VariantRefValue>(null);
            return Value(new VariantRefValue
            {
                classId = variant.ClassId,
                variantId = variant.VariantId,
                rowValueId = variant.RowValueId,
            });
        }

        /// <summary>P68 §6 — writes an unbound lookup-variant handle.</summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static NeoValueWritePayload? VariantValue<T, TValue>(
            NeoLookupVariant<T, TValue>? variant)
            where T : NeoGeneratedClassValue
            where TValue : NeoGeneratedClassValue
        {
            if (variant is null) return Value<VariantRefValue>(null);
            return Value(new VariantRefValue
            {
                classId = variant.ClassId,
                variantId = variant.VariantId,
                rowValueId = null,
            });
        }

        /// <summary>
        /// P67 §4.2 — the seam generated `ToVariant` methods call. Application
        /// is always in place; the value is <paramref name="source"/>.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static T ApplyVariant<T>(T source, NeoVariant<T> variant)
            where T : NeoGeneratedClassValue
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (variant is null) throw new ArgumentNullException(nameof(variant));
            return variant.Apply(source);
        }

        /// <summary>P68 §4.2 — applies a lookup variant in place.</summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public static T ApplyLookupVariant<T, TValue>(
            T source,
            NeoLookupVariant<T, TValue> variant,
            TValue value)
            where T : NeoGeneratedClassValue
            where TValue : NeoGeneratedClassValue
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (variant is null) throw new ArgumentNullException(nameof(variant));
            if (value is null) throw new ArgumentNullException(nameof(value));
            return variant.Apply(source, value);
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

        /// <summary>
        /// Generated low-level constructor entry point: a pre-built row graph
        /// published as one Session instance.
        ///
        /// <para>This overload is called from generated C#, which is by
        /// definition the OUTERMOST construction frame — there is no ambient
        /// NeoScript context to inherit, so a fresh evaluation context is
        /// seeded and the depth cap counts from zero. Every path that runs
        /// inside an ongoing construction goes through
        /// <see cref="CreateWritableClassValueCore"/> (or
        /// <see cref="CreateSuppliedClassValue"/>) with the caller's live
        /// scope instead, so no recursion can silently re-seed the guards.
        /// </para>
        /// </summary>
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
                new NeoConstructionScope(client, null));
        }

        /// <summary>
        /// P61 declaration-only validation seam. Animation definitions are
        /// validated before a concrete owner instance exists, but a
        /// declaration list may contain an init-backed Class row. Evaluate a
        /// self-contained initializer into a temporary Session graph so the
        /// validator sees the same concrete class and fields a real instance
        /// would receive. Parameterized declaration rows cannot be invoked
        /// honestly without their enclosing constructor arguments, so callers
        /// defer those rows until a real instance supplies the parameters.
        /// </summary>
        internal sealed class DeclarationValidationMaterialization : IDisposable
        {
            private NeoClient? client;
            private readonly string? temporarySessionRootId;

            internal DeclarationValidationMaterialization(
                NeoClient client,
                NeoMemberClass value,
                string? temporarySessionRootId)
            {
                this.client = client;
                this.temporarySessionRootId = temporarySessionRootId;
                Value = value;
            }

            internal NeoMemberClass Value { get; }

            public void Dispose()
            {
                NeoClient? activeClient = client;
                if (activeClient is null) return;
                client = null;
                ReleaseValidationMaterialization(
                    activeClient,
                    temporarySessionRootId);
            }
        }

        internal static DeclarationValidationMaterialization?
            TryResolveDeclarationForValidation(
                NeoClient client,
                NeoMemberClass declaration)
        {
            if (string.IsNullOrEmpty(declaration.overrideValueId)
                || !client.TryGetValue(
                    declaration.overrideValueId!,
                    out MemberValue? declarationRow)
                || declarationRow.init is null)
            {
                return new DeclarationValidationMaterialization(
                    client,
                    declaration,
                    temporarySessionRootId: null);
            }

            InitializerBody init = declarationRow.init;
            if (init.compiled?.parameters is { Length: > 2 })
            {
                return null;
            }
            if (declaration.member is not ClassMember classMember)
            {
                throw new InvalidOperationException(
                    $"Initializer-backed declaration row '{declarationRow.id}' is not owned by a Class member.");
            }

            var scope = new NeoConstructionScope(client, null);
            object? produced = scope.EvaluateInitializer(classMember, init);
            NeoConstructorValueReference? reference = scope.ValueReference(produced);
            if (reference is null || string.IsNullOrEmpty(reference.Value.valueId))
            {
                throw new InvalidOperationException(
                    $"Initializer-backed Class declaration row '{declarationRow.id}' produced no Neo value.");
            }
            NeoValueOwnership ownership = reference.Value.ownership
                ?? (client.TryGetValueOwnership(
                    reference.Value.valueId,
                    out NeoValueOwnership inferred)
                        ? inferred
                        : throw new InvalidOperationException(
                            $"Initializer-backed Class declaration row '{declarationRow.id}' produced missing value '{reference.Value.valueId}'."));
            var resolved = new NeoMemberClassWritable(
                client,
                classMember,
                reference.Value.valueId,
                ownership);
            string? temporarySessionRootId =
                ownership == NeoValueOwnership.Session
                && scope.IsAllocatedSessionRoot(reference.Value.valueId)
                    ? reference.Value.valueId
                    : null;
            return new DeclarationValidationMaterialization(
                client,
                resolved,
                temporarySessionRootId);
        }

        /// <summary>
        /// Reclaims a graph created solely for declaration validation. Existing
        /// Asset/Save/Session references never receive a cleanup id and are
        /// therefore left untouched.
        /// </summary>
        private static void ReleaseValidationMaterialization(
            NeoClient client,
            string? temporarySessionRootId)
        {
            if (string.IsNullOrEmpty(temporarySessionRootId)) return;
            IReadOnlyCollection<string> removed =
                client.RemoveTemporaryWritableValueGraph(
                    NeoValueOwnership.Session,
                    temporarySessionRootId!);
            if (removed.Count > 0)
            {
                client.DisposeWrappersTouchingRows(removed);
            }
        }

        private static NeoMemberClassWritable CreateWritableClassValueCore(
            NeoClient client,
            string classId,
            Dictionary<string, string> value,
            IReadOnlyList<MemberValue> valueRows,
            NeoConstructionScope scope,
            bool requireCompleteRoot = true)
        {
            RuntimeConstructedClassValue constructed =
                CreateWritableClassValueDataCore(
                    client,
                    classId,
                    value,
                    valueRows,
                    scope,
                    requireCompleteRoot);
            return new NeoMemberClassWritable(
                client,
                constructed.member,
                constructed.value.id,
                NeoValueOwnership.Session);
        }

        private static RuntimeConstructedClassValue
            CreateWritableClassValueDataCore(
            NeoClient client,
            string classId,
            Dictionary<string, string> value,
            IReadOnlyList<MemberValue> valueRows,
            NeoConstructionScope scope,
            bool requireCompleteRoot = true,
            bool trustedRuntimeRows = false,
            RuntimeClassPlan? trustedRootPlan = null)
        {
            if (!trustedRuntimeRows)
            {
                ValidateConstructibleNeoSchemaClass(client, classId);
            }
            string nowIso = scope.ExistingEvaluationContext is { } timestampContext
                ? timestampContext.allocationTracker.ConstructionTimestamp
                : DateTime.UtcNow.ToString("o");
            var rows = new List<MemberValue>(valueRows);
            var parentRow = CreateWritableClassValueRow(
                client,
                classId,
                value,
                rows,
                nowIso,
                scope,
                // The root's canonical path. PrepareConstructedGraph anchors
                // its own traversal at `root.classId` and extends it the same
                // way, so every ownership key recorded below lands on the key
                // the preflight later looks up.
                classId,
                classPlan: trustedRootPlan);
            rows.Add(parentRow);

            PrepareConstructedGraph(
                client,
                parentRow,
                rows,
                scope,
                requireCompleteRoot,
                trustedRuntimeRows,
                trustedRootPlan);
            if (scope.ExistingEvaluationContext is { } evaluationContext)
            {
                evaluationContext.allocationTracker
                    .ConsumeCreatedSessionRows(rows);
            }

            client.PublishConstructedSessionRows(rows);

            ClassMember factoryMember = trustedRuntimeRows
                ? (trustedRootPlan ?? ResolveRuntimeClassPlan(client, classId))
                    .factoryMember
                : new ClassMember
                {
                    id = $"__neo_factory_class_{classId}",
                    name = "Factory",
                    kind = MemberKind.Class,
                    classId = classId,
                    createdAt = nowIso,
                    updatedAt = nowIso,
                };
            return new RuntimeConstructedClassValue(parentRow, factoryMember);
        }

        /// <summary>
        /// Materializes generated public-constructor arguments through the
        /// same recursive, atomic supplied-value path as NeoScript
        /// <c>new Class(...)</c>. Optional null arguments are omitted, while
        /// null entries inside collections retain their position/key as an
        /// explicit nullable value row.
        ///
        /// <para>Like the row-graph overload above, this is a generated-C#
        /// entry point and therefore the outermost construction frame: a fresh
        /// evaluation context is seeded because there is no caller context to
        /// inherit.</para>
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
            AssertMemberWiseConstructionIsAvailable(client, classId);
            List<RuntimeConstructorField> fields = BuildGeneratedConstructorFields(
                suppliedValues,
                nameof(suppliedValues));
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

        /// <summary>
        /// Converts generated-C# constructor values into the runtime's field
        /// shape. Shared by the member-wise factory and the
        /// declared-constructor seam so the two reject a null descriptor
        /// identically rather than one of them dereferencing it.
        /// </summary>
        private static List<RuntimeConstructorField> BuildGeneratedConstructorFields(
            NeoGeneratedConstructorValue[] suppliedValues,
            string parameterName)
        {
            var fields = new List<RuntimeConstructorField>(suppliedValues.Length);
            foreach (NeoGeneratedConstructorValue supplied in suppliedValues)
            {
                if (supplied is null)
                {
                    throw new ArgumentException(
                        "Generated constructor arguments cannot contain null descriptors.",
                        parameterName);
                }
                fields.Add(new RuntimeConstructorField
                {
                    schemaKey = supplied.schemaKey,
                    memberId = supplied.memberId,
                    value = supplied.value,
                });
            }
            return fields;
        }

        /// <summary>
        /// Applies the generated-C# "null means omitted" rule, matching the
        /// same test in <see cref="CreateSuppliedClassValue"/>: an optional
        /// member handed a null was never supplied by the caller, so its
        /// ordinary initializer and default behavior stands rather than being
        /// cleared. A required member keeps its null so the write path reports
        /// it against the member instead of silently dropping the field.
        /// </summary>
        private static List<RuntimeConstructorField> OmitUnsuppliedOptionalFields(
            IReadOnlyList<RuntimeConstructorField> fields,
            IReadOnlyDictionary<string, Member> membersBySchemaKey)
        {
            var supplied = new List<RuntimeConstructorField>(fields.Count);
            foreach (RuntimeConstructorField field in fields)
            {
                Member member = membersBySchemaKey[field.schemaKey];
                if (field.value is null
                    && !RequiresRuntimeConstructorArgument(member))
                {
                    continue;
                }
                supplied.Add(field);
            }
            return supplied;
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
            internal string parentValueId = null!;
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
            NeoConstructionScope scope,
            bool requireCompleteRoot = true,
            bool trustedMaterialization = false,
            RuntimeClassPlan? trustedRootPlan = null)
        {
            IReadOnlyDictionary<string, NeoValueOwnership>
                referenceOwnershipByPath = scope.referenceOwnershipByPath;
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
                if (!trustedMaterialization
                    && client.TryGetValue(row.id, out MemberValue? _))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph row id '{row.id}' collides with an existing value.");
                }
            }

            var reachableStagedIds = new HashSet<string> { root.id };
            var ownedByPath = new Dictionary<string, string>();
            var parentByChildId = new Dictionary<string, string>();
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
                parentByChildId,
                pending,
                path: root.classId!,
                new HashSet<string>(),
                referenceOwnershipByPath,
                requireCompleteRoot,
                trustedMaterialization,
                trustedRootPlan);

            if (!trustedMaterialization)
            {
                foreach (string stagedId in stagedById.Keys)
                {
                    if (!reachableStagedIds.Contains(stagedId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed value graph contains orphan staged row '{stagedId}'.");
                    }
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
                    && !(scope.ExistingEvaluationContext is { } evaluationContext
                        && evaluationContext.allocationTracker
                            .IsKnownParentlessAllocatedRoot(
                                reference.sourceValueId))
                    && client.TryFindOwnedParent(
                        ownership,
                        reference.sourceValueId,
                        out string? parentValueId)
                    && !client.IsReferenceOwnedByReplayingVirtualInstance(
                        ownership,
                        reference.sourceValueId))
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
                    bool isKnownParentlessConstructorRoot =
                        reference.sourceOwnership == NeoValueOwnership.Session
                        && existedInSession
                        && scope.ExistingEvaluationContext is { } importContext
                        && importContext.allocationTracker
                            .IsKnownParentlessAllocatedRoot(
                                reference.sourceValueId);
                    string importedValueId = isKnownParentlessConstructorRoot
                        ? reference.sourceValueId
                        : reference.sourceOwnership == NeoValueOwnership.Session
                            ? client.ImportValueReference(
                                NeoValueOwnership.Session,
                                reference.sourceValueId,
                                client.IsReferenceOwnedByReplayingVirtualInstance(
                                    NeoValueOwnership.Session,
                                    reference.sourceValueId)
                                        ? reference.sourceValueId
                                        : null)
                            : client.CloneOwnedValueReferenceForNewParent(
                                NeoValueOwnership.Session,
                                reference.sourceOwnership,
                                reference.sourceValueId,
                                reference.member);
                    parentByChildId.Remove(reference.sourceValueId);
                    parentByChildId[importedValueId] = reference.parentValueId;
                    reference.replaceValueId(importedValueId);
                    if ((reference.sourceOwnership != NeoValueOwnership.Session
                            || !existedInSession)
                        && client.HasWritableValue(
                            NeoValueOwnership.Session,
                            importedValueId))
                    {
                        newlyImportedRoots.Add(importedValueId);
                    }
                    bool retainsValidatedConstructionStamp =
                        isKnownParentlessConstructorRoot
                        && scope.ExistingEvaluationContext is { } stampContext
                        && client.TryGetValue(
                            NeoValueOwnership.Session,
                            importedValueId,
                            out MemberValue? importedRow)
                        && stampContext.allocationTracker
                            .IsKnownNormalizedParentlessGraph(
                                reference.sourceValueId,
                                importedRow!,
                                reference.expectedMapKey,
                                reference.expectedContainerId);
                    if (!retainsValidatedConstructionStamp)
                    {
                        StampImportedConstructorGraph(
                            client,
                            importedValueId,
                            reference.member,
                            reference.expectedMapKey,
                            new HashSet<string>(),
                            expectedContainerId: reference.expectedContainerId);
                    }
                }
                if (scope.ExistingEvaluationContext is { } evaluationContext)
                {
                    evaluationContext.allocationTracker
                        .RegisterConstructedParents(parentByChildId);
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
            Dictionary<string, string> parentByChildId,
            List<PendingConstructorReference> pending,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath,
            bool requireRequiredMembers = true,
            bool trustedMaterialization = false,
            RuntimeClassPlan? knownClassPlan = null)
        {
            if (!traversal.Add(row.id))
            {
                throw new InvalidOperationException(
                    $"Constructed value graph contains an owned cycle at '{path}'/'{row.id}'.");
            }
            try
            {
                if (!trustedMaterialization
                    && !IsAssignableNeoSchemaClass(client, classId, classId))
                {
                    throw new InvalidOperationException(
                    $"Constructed Class row '{row.id}' has unknown runtime class '{classId}'.");
                }
                if (!trustedMaterialization && row.value is null)
                {
                    throw new InvalidOperationException(
                        $"Constructed Class root '{path}' cannot have a null record payload.");
                }
                RuntimeClassPlan classPlan = knownClassPlan
                    ?? ResolveRuntimeClassPlan(client, classId);
                IList<MergedSchemaEntry> schema = classPlan.schema;
                Dictionary<string, MergedSchemaEntry> schemaByKey =
                    classPlan.schemaByKey;
                if (!trustedMaterialization)
                {
                    foreach (string key in row.value.Keys)
                    {
                        if (!schemaByKey.ContainsKey(key))
                        {
                            throw new InvalidOperationException(
                                $"Constructed Class row '{path}' contains unknown schema key '{key}'.");
                        }
                    }
                }

                IReadOnlyDictionary<string, NeoGenericEnvEntry> env =
                    classPlan.genericEnv;
                foreach (MergedSchemaEntry entry in schema)
                {
                    Member member =
                        classPlan.membersBySchemaKey[entry.schemaKey];
                    if (!IsStoredConstructorMember(member))
                    {
                        if (!trustedMaterialization
                            && row.value.ContainsKey(entry.schemaKey))
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
                        // P43 §6.1: a declared constructor's body (step 3) and
                        // its call site's initializer block (step 4) both run
                        // AFTER this graph is prepared, so the root is
                        // legitimately incomplete here. Completeness is
                        // re-asserted on the finished instance instead — see
                        // AssertDeclaredConstructorRootIsComplete. Nested rows
                        // always keep the check; nothing writes into them
                        // between preparation and publication.
                        // P75 replay parity: the web's replay omits a required
                        // member it cannot construct — the sparse root's
                        // materialized rows supply it in the overlay, and the
                        // server-side collapse verifier proved MERGED
                        // completeness. Requiring construction-only
                        // completeness here rejected corpora the server
                        // verified at zero flags (HelloWorld's stamped Assets
                        // root).
                        if (!trustedMaterialization
                            && member.required
                            && requireRequiredMembers
                            && !client.IsReplayingVirtualInstance)
                        {
                            throw new InvalidOperationException(
                                $"Constructed Class row '{path}' is missing required member '{entry.schemaKey}'/'{entry.memberId}'.");
                        }
                        continue;
                    }
                    if (!trustedMaterialization
                        && string.IsNullOrEmpty(childId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Class row '{path}.{entry.schemaKey}' references an empty value id.");
                    }
                    string key = entry.schemaKey;
                    bool childIsTrustedStaged = trustedMaterialization
                        && stagedById.ContainsKey(childId);
                    ValidateConstructedValueLink(
                        client,
                        member,
                        childId,
                        childIsTrustedStaged
                            ? null
                            : replacement => row.value[key] = replacement,
                        row.mapKey,
                        classId,
                        stagedById,
                        reachableStagedIds,
                        ownedByPath,
                        parentByChildId,
                        pending,
                        row.id,
                        childIsTrustedStaged
                            && referenceOwnershipByPath is { Count: 0 }
                            ? path
                            : $"{path}.{entry.schemaKey}",
                        traversal,
                        env,
                        referenceOwnershipByPath,
                        trustedMaterialization);
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
            Action<string>? replaceValueId,
            string? parentMapKey,
            string? parentClassId,
            IReadOnlyDictionary<string, MemberValue> stagedById,
            HashSet<string> reachableStagedIds,
            Dictionary<string, string> ownedByPath,
            Dictionary<string, string> parentByChildId,
            List<PendingConstructorReference> pending,
            string parentValueId,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath,
            bool trustedMaterialization = false,
            string? expectedContainerId = null)
        {
            bool isStaged = stagedById.TryGetValue(
                valueId,
                out MemberValue? row);
            if ((!trustedMaterialization || !isStaged)
                && ownedByPath.TryGetValue(valueId, out string? priorPath))
            {
                throw new InvalidOperationException(
                    $"Constructed value '{valueId}' would have two owned parents ('{priorPath}' and '{path}'). Call Clone() explicitly for a second owner.");
            }
            if (!trustedMaterialization || !isStaged)
            {
                ownedByPath[valueId] = path;
            }
            parentByChildId[valueId] = parentValueId;
            string? expectedMapKey = client.ResolveCreatedValueMapKey(
                member,
                parentMapKey,
                parentClassId);

            if (!isStaged)
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
                    parentValueId = parentValueId,
                    replaceValueId = replaceValueId
                        ?? throw new InvalidOperationException(
                            $"Constructed external Class field '{path}' has no replacement target."),
                });
                return;
            }

            if (!trustedMaterialization)
            {
                reachableStagedIds.Add(valueId);
            }
            if (!trustedMaterialization
                && !MapKeyCanMoveTo(row.mapKey, expectedMapKey))
            {
                throw new InvalidOperationException(
                    $"Constructed field '{path}' carries partition '{row.mapKey ?? "main"}' but resolves to '{expectedMapKey ?? "main"}'.");
            }
            row.mapKey = expectedMapKey;
            if (expectedContainerId is not null)
            {
                if (!trustedMaterialization
                    && !string.IsNullOrEmpty(row.containerId)
                    && row.containerId != expectedContainerId)
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{path}' already belongs to unordered list '{row.containerId}', expected '{expectedContainerId}'.");
                }
                row.containerId = expectedContainerId;
            }
            if (!trustedMaterialization)
            {
                ValidateConstructedRowShape(client, member, row, path);
            }

            switch (member)
            {
                case ClassMember classMember
                    when row is ObjectMemberValue classRow
                    && classRow.value is not null:
                {
                    string actualClassId = classRow.classId
                        ?? classMember.classId;
                    if (!trustedMaterialization
                        && !IsAssignableNeoSchemaClass(
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
                        parentByChildId,
                        pending,
                        path,
                        traversal,
                        referenceOwnershipByPath,
                        trustedMaterialization: trustedMaterialization);
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
                        bool childIsTrustedStaged = trustedMaterialization
                            && stagedById.ContainsKey(memberIds[index]);
                        ValidateConstructedValueLink(
                            client,
                            entryMember,
                            memberIds[index],
                            childIsTrustedStaged
                                ? null
                                : isUnordered
                                ? _ => { }
                                : replacement => listRow.value[capturedIndex] = replacement,
                            listRow.mapKey,
                            listRow.classId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            parentByChildId,
                            pending,
                            listRow.id,
                            childIsTrustedStaged
                                && referenceOwnershipByPath is { Count: 0 }
                                ? path
                                : $"{path}[{index}]",
                            traversal,
                            env,
                            referenceOwnershipByPath,
                            trustedMaterialization,
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
                        bool childIsTrustedStaged = trustedMaterialization
                            && stagedById.ContainsKey(dictionaryRow.value[key]);
                        ValidateConstructedValueLink(
                            client,
                            entryMember,
                            dictionaryRow.value[key],
                            childIsTrustedStaged
                                ? null
                                : replacement =>
                                    dictionaryRow.value[capturedKey] = replacement,
                            dictionaryRow.mapKey,
                            dictionaryRow.classId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            parentByChildId,
                            pending,
                            dictionaryRow.id,
                            childIsTrustedStaged
                                && referenceOwnershipByPath is { Count: 0 }
                                ? path
                                : $"{path}[{key}]",
                            traversal,
                            env,
                            referenceOwnershipByPath,
                            trustedMaterialization);
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
                    var env = ResolveRuntimeInstanceEnv(
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
        ///
        /// <para><paramref name="constructionCtx"/> is the caller's LIVE
        /// evaluator context, already carrying this construction's frame. It
        /// has to be threaded rather than re-seeded: a member initializer met
        /// while filling defaults evaluates on it, and a fresh context would
        /// reset the construction stack, the NSFunction call stack, the
        /// dialogue memory store, <c>__context__</c>, and the allocation
        /// tracker — turning a cyclic graph into unbounded native recursion
        /// instead of a depth-cap diagnostic.</para>
        /// </summary>
        internal static RuntimeConstructedClassValue CreateRuntimeClassValue(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference,
            NeoScript.NSGetterEvaluator.Context constructionCtx)
        {
            AssertMemberWiseConstructionIsAvailable(
                client,
                classTypeInfo.classId);
            return CreateSuppliedClassValueData(
                client,
                classTypeInfo,
                fields,
                valueReference,
                new NeoConstructionScope(client, constructionCtx),
                trustedRuntimeRows: true);
        }

        /// <summary>
        /// Materializes a constructor whose immutable schema/field metadata was
        /// already validated before its value expressions ran. Keeping the plan
        /// avoids resolving and validating the same merged schema a second time
        /// for every eager nested constructor.
        /// </summary>
        internal static RuntimeConstructedClassValue CreateRuntimeClassValue(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            RuntimeConstructorMetadata metadata,
            Func<object?, NeoConstructorValueReference?> valueReference,
            NeoScript.NSGetterEvaluator.Context constructionCtx)
        {
            return CreateSuppliedClassValueData(
                client,
                classTypeInfo,
                fields,
                valueReference,
                new NeoConstructionScope(client, constructionCtx),
                validatedMetadata: metadata,
                trustedRuntimeRows: true);
        }

        /// <summary>
        /// P49 §1.3/§1.4 — a class that declares a required constructor cannot
        /// be materialized member-wise. The header parameter list <i>is</i> the
        /// statement that those values are not optional, so
        /// <c>new Foo { Bar = "bar" }</c> and the generated member-wise factory
        /// — the two paths that settle members without invoking a
        /// constructor — are both closed. Settling a member is not the same as
        /// satisfying the constructor, and a class with two construction
        /// contracts is the incoherence P49 exists to remove.
        ///
        /// <para>The compiler rejects both call sites, so reaching here means
        /// stale generated code or stale IR. An unresolvable class is left to
        /// the ordinary missing-class diagnostic rather than reported twice.
        /// </para>
        /// </summary>
        private static void AssertMemberWiseConstructionIsAvailable(
            NeoClient client,
            string classId)
        {
            if (!client.TryGetClass(classId, out NeoSchemaClass? schemaClass))
            {
                return;
            }
            string? requiredConstructorId = schemaClass!.requiredConstructorId;
            if (string.IsNullOrEmpty(requiredConstructorId)) return;
            ConstructorRecord record = RequireConstructorRecord(
                client,
                classId,
                schemaClass.name,
                requiredConstructorId!);
            throw new InvalidOperationException(
                $"Class '{schemaClass.name}' declares a required constructor ({RequiredConstructorParameters(record)}), so it cannot be constructed without one.");
        }

        /// <summary>
        /// The required constructor's parameter names, in declaration order —
        /// the shortest statement of what a caller has to supply, and shared by
        /// both §1.3 rejections so they name the contract identically.
        /// </summary>
        private static string RequiredConstructorParameters(
            ConstructorRecord record)
        {
            var parameters = new List<string>(record.argumentTypes.Length);
            foreach (FunctionArgumentTypeInfo argument in record.argumentTypes)
            {
                parameters.Add(argument.name);
            }
            return string.Join(", ", parameters);
        }

        private static NeoMemberClassWritable CreateSuppliedClassValue(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference,
            NeoConstructionScope? constructionScope = null,
            bool requireSuppliedRequiredFields = true,
            bool requireCompleteRoot = true,
            RuntimeConstructorMetadata? validatedMetadata = null)
        {
            RuntimeConstructedClassValue constructed =
                CreateSuppliedClassValueData(
                    client,
                    classTypeInfo,
                    fields,
                    valueReference,
                    constructionScope,
                    requireSuppliedRequiredFields,
                    requireCompleteRoot,
                    validatedMetadata);
            return new NeoMemberClassWritable(
                client,
                constructed.member,
                constructed.value.id,
                NeoValueOwnership.Session);
        }

        private static RuntimeConstructedClassValue
            CreateSuppliedClassValueData(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference,
            NeoConstructionScope? constructionScope = null,
            bool requireSuppliedRequiredFields = true,
            bool requireCompleteRoot = true,
            RuntimeConstructorMetadata? validatedMetadata = null,
            bool trustedRuntimeRows = false)
        {
            RuntimeConstructorMetadata metadata = validatedMetadata
                ?? ValidateRuntimeClassConstructorMetadataCore(
                    client,
                    classTypeInfo,
                    fields,
                    requireSuppliedRequiredFields);

            NeoConstructionScope scope = constructionScope
                ?? new NeoConstructionScope(client, null);
            var value = new Dictionary<string, string>();
            var rows = new List<MemberValue>();
            string? nowIso = null;
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
                    nowIso ??= scope.ExistingEvaluationContext is { } fieldContext
                        ? fieldContext.allocationTracker.ConstructionTimestamp
                        : DateTime.UtcNow.ToString("o"),
                    valueReference,
                    metadata.genericEnv,
                    $"{classTypeInfo.classId}.{field.schemaKey}",
                    scope.referenceOwnershipByPath);
                if (fieldValueId is not null)
                {
                    value[field.schemaKey] = fieldValueId;
                }
            }
            return CreateWritableClassValueDataCore(
                client,
                classTypeInfo.classId,
                value,
                rows,
                scope,
                requireCompleteRoot,
                trustedRuntimeRows,
                trustedRuntimeRows ? metadata.classPlan : null);
        }

        // -------------------------------------------------------------------
        // P43 §6 — declared constructors.
        // -------------------------------------------------------------------

        /// <summary>
        /// One link of a resolved constructor chain: the record to run plus the
        /// base link its <c>: base(...)</c> clause selected. A null
        /// <see cref="record"/> is the implicit <c>new()</c> — member
        /// initializers only, no body (§6.1.2).
        /// </summary>
        internal sealed class NeoResolvedConstructorLink
        {
            internal ConstructorRecord? record;
            internal NeoResolvedConstructorLink? baseLink;
            /// <summary>
            /// For each of this record's base arguments, the index of the base
            /// parameter it binds. Position-aligned with
            /// <c>record.baseArguments</c> / <c>record.compiledBaseArguments</c>.
            /// </summary>
            internal int[] baseArgumentTargets = Array.Empty<int>();
        }

        /// <summary>
        /// Everything a declared-constructor call needs, resolved and validated
        /// before any argument or field expression is allowed to run.
        /// </summary>
        internal sealed class NeoResolvedDeclaredConstructor
        {
            internal NeoClient client = null!;
            internal ClassTypeInfo classTypeInfo = null!;
            internal NeoSchemaClass schemaClass = null!;
            internal NeoResolvedConstructorLink link = null!;
            internal Dictionary<string, Member> membersBySchemaKey = null!;
            /// <summary>
            /// The class's closed generic environment, carried so a value
            /// supplied by generated C# can be expanded against the same
            /// substituted entry members the member-wise factory uses
            /// (P49 §4.4).
            /// </summary>
            internal IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                null!;
        }

        /// <summary>
        /// P43 §6.1 — resolves and fully validates a declared-constructor call:
        /// the class and its merged schema, the call-site initializer fields,
        /// the named overload, its parameter set, and the entire base chain.
        /// Deliberately value-free so callers can run it BEFORE evaluating a
        /// single argument, which is what keeps stale IR from triggering
        /// argument side effects.
        /// </summary>
        internal static NeoResolvedDeclaredConstructor ResolveDeclaredConstructor(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            string? constructorId,
            IReadOnlyList<string> argumentNames,
            IReadOnlyList<RuntimeConstructorField> fields)
        {
            RuntimeConstructorMetadata metadata =
                ValidateRuntimeClassConstructorMetadataCore(
                    client,
                    classTypeInfo,
                    fields,
                    requireSuppliedRequiredFields: false);
            if (!client.TryGetClass(classTypeInfo.classId, out NeoSchemaClass? schemaClass))
            {
                throw new InvalidOperationException(
                    $"Declared constructor references missing class '{classTypeInfo.classId}'.");
            }

            NeoResolvedConstructorLink link;
            if (constructorId is null)
            {
                AssertImplicitConstructionIsAvailable(client, schemaClass!);
                if (argumentNames.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"Declared constructor call on '{schemaClass!.name}' supplies {argumentNames.Count} arguments but resolved to the implicit parameterless constructor. Regenerate the NeoScript IR from the current schema.");
                }
                link = new NeoResolvedConstructorLink();
            }
            else
            {
                ConstructorRecord record = RequireConstructorRecord(
                    client,
                    classTypeInfo.classId,
                    schemaClass!.name,
                    constructorId);
                AssertDeclaredArgumentNamesMatch(
                    schemaClass.name,
                    record,
                    argumentNames);
                link = ResolveConstructorLink(client, record, new HashSet<string>());
            }
            AssertBaseInitializerFieldsResolve(
                link,
                metadata.membersBySchemaKey,
                schemaClass!.name);

            return new NeoResolvedDeclaredConstructor
            {
                client = client,
                classTypeInfo = classTypeInfo,
                schemaClass = schemaClass!,
                link = link,
                membersBySchemaKey = metadata.membersBySchemaKey,
                genericEnv = metadata.genericEnv,
            };
        }

        private static ConstructorRecord RequireConstructorRecord(
            NeoClient client,
            string classId,
            string className,
            string constructorId)
        {
            if (!client.TryGetConstructor(constructorId, out ConstructorRecord? record))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{constructorId}' on class '{className}' is missing from the export. Re-export the project from the current web app.");
            }
            if (record!.classId != classId)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{constructorId}' belongs to class '{record.classId}', not '{classId}'. Regenerate the NeoScript IR from the current schema.");
            }
            return record;
        }

        /// <summary>
        /// P49 §1.3 — a class that declares a required constructor has no
        /// implicit <c>new()</c>: the parameter list on its header is the
        /// statement that those values are not optional. The compiler rejects
        /// the call site too, so reaching this means stale IR or stale
        /// generated code — and failing here rather than constructing is what
        /// keeps a half-settled instance from being published.
        /// </summary>
        private static void AssertImplicitConstructionIsAvailable(
            NeoClient client,
            NeoSchemaClass schemaClass)
        {
            string? requiredConstructorId = schemaClass.requiredConstructorId;
            if (string.IsNullOrEmpty(requiredConstructorId)) return;
            ConstructorRecord record = RequireConstructorRecord(
                client,
                schemaClass.id,
                schemaClass.name,
                requiredConstructorId!);
            throw new InvalidOperationException(
                $"Class '{schemaClass.name}' declares a required constructor ({RequiredConstructorParameters(record)}) and cannot be constructed with the implicit parameterless new. Regenerate the NeoScript IR from the current schema.");
        }

        /// <summary>
        /// P49 §1.5 — every base-clause initializer key must name a stored
        /// member of the class under construction. Checked here, with the rest
        /// of the call shape, so a stale block fails before any expression runs
        /// — the same metadata-before-values ordering the call-site block gets.
        /// </summary>
        private static void AssertBaseInitializerFieldsResolve(
            NeoResolvedConstructorLink link,
            IReadOnlyDictionary<string, Member> membersBySchemaKey,
            string className)
        {
            for (NeoResolvedConstructorLink? current = link;
                 current is not null;
                 current = current.baseLink)
            {
                ConstructorRecord? record = current.record;
                if (record?.baseInitializerFields is null) continue;
                foreach (ConstructorBaseInitializerField field
                         in record.baseInitializerFields)
                {
                    if (!membersBySchemaKey.TryGetValue(
                            field.name,
                            out Member? member))
                    {
                        throw new InvalidOperationException(
                            $"Declared constructor '{record.id}' settles base member '{field.name}', which class '{className}' does not declare. Regenerate the NeoScript IR from the current schema.");
                    }
                    if (!IsStoredConstructorMember(member))
                    {
                        throw new InvalidOperationException(
                            $"Declared constructor '{record.id}' settles base member '{field.name}', which is not a stored member.");
                    }
                }
            }
        }

        /// <summary>
        /// P65 §2.2 subset matching: the call must cover every non-defaulted
        /// parameter and name no unknown parameter. With duplicates rejected,
        /// those two checks also bound the total at the full arity. The
        /// coverage check runs first so an uncovered required parameter keeps
        /// its pre-P65 error.
        /// </summary>
        private static void AssertDeclaredArgumentNamesMatch(
            string className,
            ConstructorRecord record,
            IReadOnlyList<string> argumentNames)
        {
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in argumentNames)
            {
                if (!supplied.Add(name))
                {
                    throw new InvalidOperationException(
                        $"Declared constructor call on class '{className}' supplies argument '{name}' more than once.");
                }
            }
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (FunctionArgumentTypeInfo argument in record.argumentTypes)
            {
                declared.Add(argument.name);
                if (supplied.Contains(argument.name)) continue;
                if (NeoParameterDefaults.HasDefault(argument)) continue;
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' on class '{className}' is missing argument '{argument.name}'. Regenerate the NeoScript IR from the current schema.");
            }
            foreach (string name in argumentNames)
            {
                if (declared.Contains(name)) continue;
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' on class '{className}' names unknown argument '{name}'. Regenerate the NeoScript IR from the current schema.");
            }
        }

        /// <summary>
        /// P43 §6.1 — walks the <c>: base(...)</c> chain, resolving each base
        /// overload by argument-name set. A class that declares constructors
        /// but no parameterless one must be reached through an explicit clause;
        /// otherwise the base's parameterless constructor runs implicitly, as
        /// in C#.
        /// </summary>
        internal static NeoResolvedConstructorLink ResolveConstructorLink(
            NeoClient client,
            ConstructorRecord record,
            HashSet<string> visited)
        {
            if (!visited.Add(record.id))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' has a cyclic base chain.");
            }
            var link = new NeoResolvedConstructorLink { record = record };
            if (!client.TryGetClass(record.classId, out NeoSchemaClass? owningClass))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' references missing class '{record.classId}'.");
            }
            string? baseClassId = owningClass!.extendsClassId;
            ConstructorBaseArgument[] baseArguments =
                record.baseArguments ?? Array.Empty<ConstructorBaseArgument>();
            AssertBaseInitializerBlockIsCompiled(record, owningClass, baseClassId);

            if (baseArguments.Length == 0)
            {
                ConstructorRecord? implicitBase = ResolveImplicitBaseConstructor(
                    client,
                    owningClass,
                    record);
                if (implicitBase is not null)
                {
                    link.baseLink = ResolveConstructorLink(client, implicitBase, visited);
                }
                return link;
            }

            if (string.IsNullOrEmpty(baseClassId))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' on class '{owningClass.name}' has a base clause but the class extends nothing.");
            }
            FunctionWithReturnType[] compiled =
                record.compiledBaseArguments
                ?? throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' has base arguments with no compiled getters. Re-export the project from the current web app.");
            if (compiled.Length != baseArguments.Length)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' has {baseArguments.Length} base arguments but {compiled.Length} compiled base getters. Re-export the project from the current web app.");
            }

            var names = new List<string>(baseArguments.Length);
            foreach (ConstructorBaseArgument argument in baseArguments)
            {
                names.Add(argument.name);
            }
            ConstructorRecord baseRecord = ResolveBaseOverloadByNameSet(
                client,
                baseClassId!,
                record,
                names);
            link.baseArgumentTargets = new int[baseArguments.Length];
            for (int i = 0; i < baseArguments.Length; i++)
            {
                link.baseArgumentTargets[i] = IndexOfArgument(
                    baseRecord,
                    baseArguments[i].name,
                    record.id);
            }
            link.baseLink = ResolveConstructorLink(client, baseRecord, visited);
            return link;
        }

        /// <summary>
        /// P49 §1.5 — a base initializer block is a base clause in its own
        /// right, so it is checked even when the clause passes no arguments:
        /// <c>: Foo { Bar = bar }</c> is the shape a base with no constructor
        /// at all is settled through.
        /// </summary>
        private static void AssertBaseInitializerBlockIsCompiled(
            ConstructorRecord record,
            NeoSchemaClass owningClass,
            string? baseClassId)
        {
            ConstructorBaseInitializerField[] baseInitializerFields =
                record.baseInitializerFields
                ?? Array.Empty<ConstructorBaseInitializerField>();
            if (baseInitializerFields.Length == 0) return;
            if (string.IsNullOrEmpty(baseClassId))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' on class '{owningClass.name}' has a base initializer block but the class extends nothing.");
            }
            FunctionWithReturnType[] compiled =
                record.compiledBaseInitializerFields
                ?? Array.Empty<FunctionWithReturnType>();
            if (compiled.Length != baseInitializerFields.Length)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' has {baseInitializerFields.Length} base initializer fields but {compiled.Length} compiled getters. Re-export the project from the current web app.");
            }
        }

        private static ConstructorRecord? ResolveImplicitBaseConstructor(
            NeoClient client,
            NeoSchemaClass owningClass,
            ConstructorRecord record)
        {
            string? baseClassId = owningClass.extendsClassId;
            if (string.IsNullOrEmpty(baseClassId)) return null;
            if (!client.TryGetClass(baseClassId!, out NeoSchemaClass? baseClass))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' extends missing class '{baseClassId}'.");
            }
            IReadOnlyList<string> baseConstructorIds =
                ResolvableConstructorIds(baseClass!);
            if (baseConstructorIds.Count == 0) return null;
            foreach (string candidateId in baseConstructorIds)
            {
                ConstructorRecord candidate = RequireConstructorRecord(
                    client,
                    baseClassId!,
                    baseClass.name,
                    candidateId);
                // P65 §2.3: a constructor whose every parameter is defaulted
                // is parameterless-callable — a zero-parameter list satisfies
                // this vacuously.
                if (AllParametersDefaulted(candidate)) return candidate;
            }
            throw new InvalidOperationException(
                $"Declared constructor '{record.id}' on class '{owningClass.name}' must call a base constructor: '{baseClass.name}' declares constructors but none parameterless.");
        }

        private static ConstructorRecord ResolveBaseOverloadByNameSet(
            NeoClient client,
            string baseClassId,
            ConstructorRecord record,
            IReadOnlyList<string> argumentNames)
        {
            if (!client.TryGetClass(baseClassId, out NeoSchemaClass? baseClass))
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' extends missing class '{baseClassId}'.");
            }
            IReadOnlyList<string> baseConstructorIds =
                ResolvableConstructorIds(baseClass!);
            var supplied = new HashSet<string>(argumentNames, StringComparer.Ordinal);
            if (supplied.Count != argumentNames.Count)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' names a base argument more than once.");
            }
            // P65 §2.2 subset matching with C#'s betterness: the clause must
            // cover every non-defaulted parameter and name no unknown
            // parameter; among the matches, the candidate filling in the
            // fewest defaults wins, and a tie is ambiguous.
            var matches = new List<ConstructorRecord>();
            foreach (string candidateId in baseConstructorIds)
            {
                ConstructorRecord candidate = RequireConstructorRecord(
                    client,
                    baseClassId,
                    baseClass.name,
                    candidateId);
                if (!ArgumentNameSetMatches(candidate, supplied)) continue;
                matches.Add(candidate);
            }
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' base call matches no constructor of class '{baseClass.name}'.");
            }
            int fewestFillIns = int.MaxValue;
            foreach (ConstructorRecord candidate in matches)
            {
                fewestFillIns = Math.Min(
                    fewestFillIns,
                    candidate.argumentTypes.Length - supplied.Count);
            }
            ConstructorRecord? match = null;
            foreach (ConstructorRecord candidate in matches)
            {
                if (candidate.argumentTypes.Length - supplied.Count != fewestFillIns)
                {
                    continue;
                }
                if (match is not null)
                {
                    throw new InvalidOperationException(
                        $"Declared constructor '{record.id}' base call is ambiguous between '{match.id}' and '{candidate.id}' on class '{baseClass.name}'.");
                }
                match = candidate;
            }
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' resolved its base call to an empty betterness set on class '{baseClass.name}'.");
            }
            return match;
        }

        /// <summary>
        /// P49 §1.1 — every constructor a class can be reached through. A
        /// required constructor is deliberately absent from
        /// <c>constructorIds</c> because it is the class's only one (§1.3), so
        /// base resolution has to consult both fields or a subclass could never
        /// call the base constructor of a class that declares one.
        /// </summary>
        private static IReadOnlyList<string> ResolvableConstructorIds(
            NeoSchemaClass schemaClass)
        {
            string[] declared = schemaClass.constructorIds ?? Array.Empty<string>();
            string? requiredConstructorId = schemaClass.requiredConstructorId;
            if (string.IsNullOrEmpty(requiredConstructorId)) return declared;
            var all = new List<string>(declared.Length + 1)
            {
                requiredConstructorId!,
            };
            all.AddRange(declared);
            return all;
        }

        /// <summary>
        /// P65 §2.2's subset rule: the supplied names must cover every
        /// non-defaulted parameter and name no unknown parameter. With no
        /// defaults in play this degenerates to P43's exact name-set match.
        /// </summary>
        private static bool ArgumentNameSetMatches(
            ConstructorRecord record,
            HashSet<string> supplied)
        {
            int covered = 0;
            foreach (FunctionArgumentTypeInfo argument in record.argumentTypes)
            {
                if (supplied.Contains(argument.name))
                {
                    covered++;
                    continue;
                }
                if (!NeoParameterDefaults.HasDefault(argument)) return false;
            }
            return covered == supplied.Count;
        }

        /// <summary>
        /// P65 §2.3 — whether every parameter is defaulted, which makes the
        /// constructor parameterless-callable. Vacuously true for an empty
        /// parameter list.
        /// </summary>
        private static bool AllParametersDefaulted(ConstructorRecord record)
        {
            foreach (FunctionArgumentTypeInfo argument in record.argumentTypes)
            {
                if (!NeoParameterDefaults.HasDefault(argument)) return false;
            }
            return true;
        }

        private static int IndexOfArgument(
            ConstructorRecord record,
            string name,
            string callerConstructorId)
        {
            for (int i = 0; i < record.argumentTypes.Length; i++)
            {
                if (record.argumentTypes[i].name == name) return i;
            }
            throw new InvalidOperationException(
                $"Declared constructor '{callerConstructorId}' binds base argument '{name}', which constructor '{record.id}' does not declare.");
        }

        /// <summary>
        /// P43 §6.1 — runs the four construction steps in order:
        /// <list type="number">
        ///   <item><description>member initializers (including computed ones),
        ///   </description></item>
        ///   <item><description>the base constructor chain, each link followed
        ///   by its own base-clause initializer block (P49 §1.5),
        ///   </description></item>
        ///   <item><description>this constructor's body against the constructed
        ///   root,</description></item>
        ///   <item><description>the call-site initializer block, which
        ///   overwrites whatever steps 1–3 wrote.</description></item>
        /// </list>
        /// Step 4 winning is deliberate and matches C#: precedence stays static
        /// rather than depending on which branch the body took. It is also what
        /// P49 §2.5 pins for the one collision the two blocks can have — a base
        /// clause and a call site setting the same inherited member — since the
        /// call site is the only one of the two visible from where the author
        /// is standing.
        /// </summary>
        internal static NeoMemberClassWritable ConstructDeclaredClassValue(
            NeoResolvedDeclaredConstructor resolved,
            IReadOnlyDictionary<string, object?> argumentValues,
            IReadOnlyList<RuntimeConstructorField> fields,
            NeoScript.NSGetterEvaluator.Context ctx,
            Action<NeoScript.NSGetterEvaluator.Context>? evaluateFieldValues = null)
        {
            NeoClient client = resolved.client;
            NeoScript.NSGetterEvaluator.Context constructionCtx =
                PushConstructionFrame(ctx, resolved.schemaClass.name);
            object?[] positionalArguments = OrderDeclaredArguments(
                resolved.link.record,
                argumentValues);
            var scope = new NeoConstructionScope(
                client,
                constructionCtx,
                positionalArguments);

            // Step 1 — member initializers. No fields are supplied here: an
            // overridden member's initializer still RUNS and is then overwritten
            // by step 4 (§1.2), which is observably different from never
            // running it.
            NeoMemberClassWritable node = CreateSuppliedClassValue(
                client,
                resolved.classTypeInfo,
                Array.Empty<RuntimeConstructorField>(),
                scope.ValueReference,
                scope,
                requireSuppliedRequiredFields: false,
                requireCompleteRoot: false);
            ObjectMemberValue root = node.value
                ?? throw new InvalidOperationException(
                    $"Declared constructor for '{resolved.classTypeInfo.classId}' produced no root row.");

            // `CreateSuppliedClassValue` PUBLISHES the whole graph, and every
            // step below can throw — a constructor body may `throw` outright.
            // A failure therefore has to reclaim what step 1 published, or the
            // rows stay in sessionData forever: the evaluator's terminal
            // reclamation sweep only walks roots it was told about, and the
            // Save-ownership garbage collector never sees a parentless Session
            // row. Reclaiming here rather than registering the root with the
            // allocation tracker is deliberate: this method also serves the
            // generated-C# seam, whose context has no enclosing execution, so a
            // registered root would be swept by the FIRST nested body's
            // allocation scope closing mid-construction.
            try
            {
                // Steps 2 and 3 — base chain then this body, both against the
                // same `this`.
                object? thisValue = NeoScript.NSGetterEvaluator.UnwrapRow(
                    root,
                    constructionCtx,
                    NeoValueOwnership.Session);
                RunDeclaredConstructorChain(
                    client,
                    resolved,
                    resolved.link,
                    positionalArguments,
                    thisValue,
                    root.id,
                    constructionCtx);

                // Step 4 — the call site wins. Its expressions are evaluated
                // HERE, after the body, exactly as C# runs an object
                // initializer once the constructor has returned: a field
                // expression that observes state the body wrote must see the
                // post-body value. (The legacy schema-derived
                // `classConstructor` arm has no body and stays eval-first on
                // both runtimes.)
                evaluateFieldValues?.Invoke(constructionCtx);
                ApplyDeclaredConstructorFields(
                    client,
                    resolved,
                    root.id,
                    fields,
                    constructionCtx);

                AssertDeclaredConstructorRootIsComplete(client, resolved, root.id);
                StampConstructedInstanceProvenance(
                    client,
                    resolved,
                    argumentValues,
                    root.id);
                node.RefreshChildrenAfterConstruction();
            }
            catch
            {
                ReclaimFailedConstruction(client, root.id, constructionCtx);
                throw;
            }
            return node;
        }

        /// <summary>
        /// P75 §4 — records the creation recipe on the row construction just
        /// produced. Only the runtime knows which overload ran and what the
        /// arguments evaluated to, so this is the one place a runtime-created
        /// instance becomes a durable sparse root: without the stamp its
        /// omitted members are frozen at their construction-time values and
        /// never track later declaration changes.
        /// </summary>
        private static void StampConstructedInstanceProvenance(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            IReadOnlyDictionary<string, object?> argumentValues,
            string rootValueId)
        {
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    rootValueId,
                    out ObjectMemberValue? live))
            {
                throw new InvalidOperationException(
                    $"Declared constructor for '{resolved.classTypeInfo.classId}' lost its root row '{rootValueId}' before creation provenance could be recorded.");
            }
            ConstructorRecord? record = resolved.link.record;
            var constructorArgs = new Dictionary<string, JToken?>(StringComparer.Ordinal);
            if (record is not null)
            {
                for (int index = 0; index < record.argumentTypes.Length; index++)
                {
                    FunctionArgumentTypeInfo argument = record.argumentTypes[index];
                    // An omitted name with a declared default is filled
                    // callee-side (P65 §2.5) and is deliberately NOT recorded:
                    // the replay re-reads the parameter's current default, so
                    // the instance keeps tracking it.
                    if (!argumentValues.TryGetValue(argument.name, out object? value))
                    {
                        continue;
                    }
                    constructorArgs[NeoClient.ConstructorParameterId(record, index)] =
                        NeoClient.ConstructorArgumentToken(
                            value,
                            $"'{argument.name}' of constructor '{record.id}' on class '{resolved.schemaClass.name}'");
                }
            }
            NeoClient.StampConstructionProvenance(live, record?.id, constructorArgs);
        }

        /// <summary>
        /// Withdraws the instance graph a failed construction had already
        /// published, using the same removal + wrapper-disposal + cache-eviction
        /// trio the evaluator's terminal reclamation sweep uses.
        /// </summary>
        private static void ReclaimFailedConstruction(
            NeoClient client,
            string rootValueId,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            IReadOnlyCollection<string> removed =
                client.RemoveTemporaryWritableValueGraph(
                    NeoValueOwnership.Session,
                    rootValueId);
            if (removed.Count == 0) return;
            client.DisposeWrappersTouchingRows(removed);
            NeoScript.NSGetterEvaluator.EvictCachedRows(
                ctx,
                NeoValueOwnership.Session,
                removed);
        }

        private static object?[] OrderDeclaredArguments(
            ConstructorRecord? record,
            IReadOnlyDictionary<string, object?> argumentValues)
        {
            if (record is null) return Array.Empty<object?>();
            var ordered = new object?[record.argumentTypes.Length];
            for (int i = 0; i < record.argumentTypes.Length; i++)
            {
                FunctionArgumentTypeInfo argument = record.argumentTypes[i];
                if (!argumentValues.TryGetValue(argument.name, out object? value))
                {
                    // P65 §2.5 callee-side fill: an omitted name binds the
                    // parameter's current stored default.
                    // `AssertDeclaredArgumentNamesMatch` already rejected
                    // omissions without one.
                    if (NeoParameterDefaults.HasDefault(argument))
                    {
                        ordered[i] = NeoParameterDefaults.DefaultRuntimeValue(
                            argument,
                            $"Constructor '{record.id}'");
                        continue;
                    }
                    throw new InvalidOperationException(
                        $"Declared constructor '{record.id}' is missing a value for argument '{argument.name}'.");
                }
                ordered[i] = value;
            }
            return ordered;
        }

        private static void RunDeclaredConstructorChain(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            NeoResolvedConstructorLink link,
            object?[] argumentValues,
            object? thisValue,
            string rootValueId,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            ConstructorRecord? record = link.record;
            if (record is null) return;

            if (link.baseLink is not null)
            {
                ConstructorRecord baseRecord = link.baseLink.record
                    ?? throw new InvalidOperationException(
                        $"Declared constructor '{record.id}' resolved an empty base link.");
                var baseArguments = new object?[baseRecord.argumentTypes.Length];
                var boundBaseSlots = new bool[baseRecord.argumentTypes.Length];
                ConstructorBaseArgument[] declaredBaseArguments =
                    record.baseArguments ?? Array.Empty<ConstructorBaseArgument>();
                FunctionWithReturnType[] compiled =
                    record.compiledBaseArguments ?? Array.Empty<FunctionWithReturnType>();
                for (int i = 0; i < declaredBaseArguments.Length; i++)
                {
                    boundBaseSlots[link.baseArgumentTargets[i]] = true;
                    // `this` is the instance under construction, not null: step
                    // 1 has already run every member initializer, so a base
                    // argument may legitimately read `this.X` as well as the
                    // parameters it was handed. This is what
                    // `evaluateBaseConstructorArguments` binds in
                    // evaluateNSGetter.ts, and binding null here would silently
                    // produce a different base member value on the same
                    // project.
                    baseArguments[link.baseArgumentTargets[i]] = ExecuteConstructorBody(
                        client,
                        compiled[i],
                        BuildConstructorScope(record, argumentValues, thisValue, ctx),
                        ctx.WithThis(thisValue),
                        expectValue: true,
                        $"Base argument '{declaredBaseArguments[i].name}' of constructor '{record.id}'");
                }
                // P65 §2.5 callee-side fill, same as a direct constructor
                // call: the base overload's own current default completes each
                // omitted slot. Base resolution already required every
                // unbound slot to be defaulted, so a bare slot here is stale
                // IR rather than a tolerable absence.
                for (int i = 0; i < baseRecord.argumentTypes.Length; i++)
                {
                    if (boundBaseSlots[i]) continue;
                    FunctionArgumentTypeInfo baseParameter =
                        baseRecord.argumentTypes[i];
                    if (!NeoParameterDefaults.HasDefault(baseParameter))
                    {
                        throw new InvalidOperationException(
                            $"Declared constructor '{record.id}' binds no argument for base parameter '{baseParameter.name}' of constructor '{baseRecord.id}'. Regenerate the NeoScript IR from the current schema.");
                    }
                    baseArguments[i] = NeoParameterDefaults.DefaultRuntimeValue(
                        baseParameter,
                        $"Constructor '{baseRecord.id}'");
                }
                RunDeclaredConstructorChain(
                    client,
                    resolved,
                    link.baseLink,
                    baseArguments,
                    thisValue,
                    rootValueId,
                    ctx);
            }

            // P49 §1.5 — the base clause's initializer block, applied once the
            // base has run and before this constructor's own body, so the body
            // and then the call site can still refine an inherited member. It
            // is applied even when the clause passed no arguments: settling the
            // base's members directly is the shape a base that declares no
            // constructor at all is reached through.
            ApplyBaseInitializerFields(
                client,
                resolved,
                record,
                argumentValues,
                thisValue,
                rootValueId,
                ctx);

            ExecuteConstructorBody(
                client,
                record.action,
                BuildConstructorScope(record, argumentValues, thisValue, ctx),
                ctx.WithThis(thisValue),
                expectValue: false,
                $"Constructor '{record.id}'");
        }

        /// <summary>
        /// Evaluates one base clause's initializer block in the declaring
        /// constructor's parameter scope and writes it through the same path as
        /// the call-site block, so a base clause replacing an inherited member
        /// unlinks the displaced child exactly as any other assignment does.
        /// </summary>
        private static void ApplyBaseInitializerFields(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            ConstructorRecord record,
            object?[] argumentValues,
            object? thisValue,
            string rootValueId,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            ConstructorBaseInitializerField[] baseInitializerFields =
                record.baseInitializerFields
                ?? Array.Empty<ConstructorBaseInitializerField>();
            if (baseInitializerFields.Length == 0) return;
            FunctionWithReturnType[] compiled =
                record.compiledBaseInitializerFields
                ?? Array.Empty<FunctionWithReturnType>();
            var fields = new List<RuntimeConstructorField>(
                baseInitializerFields.Length);
            for (int i = 0; i < baseInitializerFields.Length; i++)
            {
                ConstructorBaseInitializerField field = baseInitializerFields[i];
                Member member = resolved.membersBySchemaKey[field.name];
                fields.Add(new RuntimeConstructorField
                {
                    schemaKey = field.name,
                    memberId = member.id,
                    value = ExecuteConstructorBody(
                        client,
                        compiled[i],
                        BuildConstructorScope(record, argumentValues, thisValue, ctx),
                        ctx.WithThis(thisValue),
                        expectValue: true,
                        $"Base initializer field '{field.name}' of constructor '{record.id}'"),
                });
            }
            ApplyDeclaredConstructorFields(
                client,
                resolved,
                rootValueId,
                fields,
                ctx);
        }

        private static Dictionary<string, object?> BuildConstructorScope(
            ConstructorRecord record,
            object?[] argumentValues,
            object? thisValue,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            var scope = new Dictionary<string, object?>(argumentValues.Length + 2)
            {
                ["__this__"] = thisValue,
                ["__root__"] = ctx.rootValue,
            };
            for (int i = 0; i < argumentValues.Length; i++)
            {
                scope[$"__arg_{i}__"] = argumentValues[i];
            }
            return scope;
        }

        private static object? ExecuteConstructorBody(
            NeoClient client,
            FunctionWithReturnType body,
            Dictionary<string, object?> scope,
            NeoScript.NSGetterEvaluator.Context ctx,
            bool expectValue,
            string subject)
        {
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                body,
                scope,
                ctx);
            if (result.IsPaused)
            {
                throw new InvalidOperationException(
                    $"{subject} suspended on deferred Function '{result.SuspendedMemberId}'. A constructor cannot await.");
            }
            if (expectValue && !result.Returned)
            {
                throw new InvalidOperationException(
                    $"{subject} ended without a return statement.");
            }
            return result.ReturnValue;
        }

        /// <summary>
        /// P43 §6.1 step 4 — applies the call-site initializer block through the
        /// same write path a <c>this.X = …</c> assignment inside a body uses, so
        /// a replaced child is unlinked and an attached class value is imported
        /// exactly as it would be anywhere else.
        ///
        /// <para>A null here is an <b>explicit</b> assignment, not an omitted
        /// argument. These fields are the call site's initializer block
        /// (<c>new Foo() { Bar = null }</c>), where the author wrote the null;
        /// the "null means omit this optional parameter" sentinel belongs only
        /// to the generated-C#/schema-derived factory path, whose fields are
        /// positional parameters. So an explicit null clears an optional member
        /// and is an error on a required one.</para>
        /// </summary>
        private static void ApplyDeclaredConstructorFields(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            string rootValueId,
            IReadOnlyList<RuntimeConstructorField> fields,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            MaterializeDeclaredConstructorFieldPayloads(
                client,
                resolved,
                fields,
                ctx);
            foreach (RuntimeConstructorField field in fields)
            {
                Member member = resolved.membersBySchemaKey[field.schemaKey];
                if (field.value is null && member.required)
                {
                    throw new InvalidOperationException(
                        $"Constructor field '{field.schemaKey}' on '{resolved.schemaClass.name}' is required and cannot be null.");
                }
                NeoScriptExecutor.WriteConstructedClassMember(
                    client,
                    rootValueId,
                    field.schemaKey,
                    member,
                    NeoValueOwnership.Session,
                    field.value,
                    ctx);
                StampConstructedCollectionRow(
                    client,
                    resolved,
                    rootValueId,
                    field.schemaKey,
                    member);
            }
        }

        /// <summary>
        /// A List or Dictionary row minted by the write path carries no
        /// <c>genericBindings</c> stamp, because a write target has no view of
        /// the class's type environment — and reading that row's entries back
        /// resolves them through exactly that stamp. It is applied here, the
        /// one place where the written member and the resolved construction's
        /// environment are both in hand.
        ///
        /// <para>A no-op for a non-collection member, for an entry subtree that
        /// references no generic parameter, and for a row that already carries
        /// a stamp — the stamp is immutable.</para>
        /// </summary>
        private static void StampConstructedCollectionRow(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            string rootValueId,
            string schemaKey,
            Member member)
        {
            if (member is not ListMember && member is not DictionaryMember)
            {
                return;
            }
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    rootValueId,
                    out ObjectMemberValue? root))
            {
                return;
            }
            if (root!.value is null) return;
            if (!root.value.TryGetValue(schemaKey, out string childValueId))
            {
                return;
            }
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    childValueId,
                    out MemberValue? row))
            {
                return;
            }
            NeoGenericResolution.StampGenericBindings(
                client,
                member,
                row!,
                resolved.genericEnv);
        }

        /// <summary>
        /// Completeness is asserted on the FINISHED instance because a declared
        /// constructor's body and its call site both write after the graph is
        /// prepared (see the comment in
        /// <see cref="ValidateConstructedClassRow"/>).
        /// </summary>
        private static void AssertDeclaredConstructorRootIsComplete(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            string rootValueId)
        {
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    rootValueId,
                    out ObjectMemberValue? root)
                || root!.value is null)
            {
                throw new InvalidOperationException(
                    $"Declared constructor for '{resolved.classTypeInfo.classId}' left no readable root row.");
            }
            // P75 replay parity: a replayed sparse instance's required members
            // may be supplied by its MATERIALIZED rows in the overlay rather
            // than by construction — the web's replay omits them the same way,
            // and the server-side collapse verifier proved merged
            // completeness.
            if (client.IsReplayingVirtualInstance) return;
            foreach (MergedSchemaEntry entry in ResolveMergedSchema(
                client,
                resolved.classTypeInfo.classId))
            {
                if (!resolved.membersBySchemaKey.TryGetValue(
                        entry.schemaKey,
                        out Member? member))
                {
                    continue;
                }
                if (!IsStoredConstructorMember(member)) continue;
                if (!member.required) continue;
                if (root.value.ContainsKey(entry.schemaKey)) continue;
                throw new InvalidOperationException(
                    $"Declared constructor for '{resolved.schemaClass.name}' left required member '{entry.schemaKey}'/'{entry.memberId}' unset. Assign it in the constructor body, give it a default, or pass it at the call site.");
            }
        }

        /// <summary>
        /// P43 §8 — the seam generated C# constructors call. A null
        /// <paramref name="constructorId"/> is the implicit <c>new()</c>:
        /// member initializers only, no body, which is what a class keeps even
        /// after declaring constructors (§6.1.2).
        /// </summary>
        public static NeoMemberClassWritable EvaluateDeclaredConstructor(
            NeoClient client,
            string classId,
            string? constructorId,
            NeoDeclaredConstructorArgument[] arguments)
        {
            return EvaluateDeclaredConstructor(
                client,
                classId,
                constructorId,
                arguments,
                Array.Empty<NeoGeneratedConstructorValue>());
        }

        /// <summary>
        /// P49 §4.4 — the seam a generated C# constructor calls when its class
        /// carries members the declared parameters do not cover: the declared
        /// parameters by name in <paramref name="arguments"/>, and the
        /// remaining optional members as the call-site initializer block in
        /// <paramref name="suppliedValues"/>. The block is step 4, so a member
        /// supplied here refines whatever the initializers and the body wrote
        /// (§2.5).
        ///
        /// <para>A null in <paramref name="suppliedValues"/> means the caller
        /// omitted that optional parameter — it matches the generated
        /// <c>= null</c> default and is dropped rather than written, exactly as
        /// in <see cref="CreateWritableClassValue"/>. That is the opposite of a
        /// null in a NeoScript initializer block, where the author wrote the
        /// null and it clears the member.</para>
        /// </summary>
        public static NeoMemberClassWritable EvaluateDeclaredConstructor(
            NeoClient client,
            string classId,
            string? constructorId,
            NeoDeclaredConstructorArgument[] arguments,
            NeoGeneratedConstructorValue[] suppliedValues)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (classId is null) throw new ArgumentNullException(nameof(classId));
            arguments ??= Array.Empty<NeoDeclaredConstructorArgument>();
            suppliedValues ??= Array.Empty<NeoGeneratedConstructorValue>();

            var classTypeInfo = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = classId,
            };
            var argumentNames = new List<string>(arguments.Length);
            foreach (NeoDeclaredConstructorArgument argument in arguments)
            {
                if (argument is null)
                {
                    throw new ArgumentException(
                        "Generated constructor arguments cannot contain null descriptors.",
                        nameof(arguments));
                }
                argumentNames.Add(argument.name);
            }
            // Resolved against the FULL supplied set so a stale generated field
            // is rejected before anything is published; the omit rule is
            // applied only once the members are known.
            List<RuntimeConstructorField> fields = BuildGeneratedConstructorFields(
                suppliedValues,
                nameof(suppliedValues));

            NeoResolvedDeclaredConstructor resolved = ResolveDeclaredConstructor(
                client,
                classTypeInfo,
                constructorId,
                argumentNames,
                fields);

            var ctx = new NeoScript.NSGetterEvaluator.Context(
                client,
                null,
                null,
                valueOwnership: NeoValueOwnership.Session);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));

            var argumentValues = new Dictionary<string, object?>(arguments.Length);
            foreach (NeoDeclaredConstructorArgument argument in arguments)
            {
                argumentValues[argument.name] = MarshalDeclaredConstructorArgument(
                    client,
                    resolved,
                    argument,
                    ctx);
            }

            List<RuntimeConstructorField> callSiteFields =
                OmitUnsuppliedOptionalFields(fields, resolved.membersBySchemaKey);
            return ConstructDeclaredClassValue(
                resolved,
                argumentValues,
                callSiteFields,
                ctx);
        }

        /// <summary>
        /// Converts declared-constructor initializer fields into values
        /// <see cref="ApplyDeclaredConstructorFields"/> can write.
        ///
        /// <para>The write path directly understands scalars and Class
        /// references, but enums, lookups, files, and collections require
        /// stored payloads such as option ids, file records, and entry rows.
        /// Computing them here through the same
        /// <see cref="ComputeRuntimeConstructorPayload"/> the member-wise
        /// factory uses keeps NeoScript and generated-C# call sites on the same
        /// materialization path.</para>
        ///
        /// <para>Class-typed members are deliberately left alone: the write
        /// target already routes a Class value through the ordinary import
        /// funnel, which is the same rule
        /// <see cref="PrepareConstructedGraph"/> applies on the factory path.
        /// Class values nested <i>inside</i> a supplied collection have no such
        /// target, so they are imported here — otherwise the entry row would be
        /// referenced by a second owner without ever being adopted.</para>
        /// </summary>
        private static void MaterializeDeclaredConstructorFieldPayloads(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            IReadOnlyList<RuntimeConstructorField> fields,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            if (fields.Count == 0) return;
            string nowIso = DateTime.UtcNow.ToString("o");
            foreach (RuntimeConstructorField field in fields)
            {
                Member member = resolved.membersBySchemaKey[field.schemaKey];
                // A null that survived the omit filter belongs to a required
                // member; ApplyDeclaredConstructorFields names it. A Class
                // member is the write target's own business.
                if (field.value is null) continue;
                if (member is ClassMember) continue;
                var stagedRows = new List<MemberValue>();
                object? payload = ComputeRuntimeConstructorPayload(
                    client,
                    member,
                    field.value,
                    stagedRows,
                    nowIso,
                    value => ImportedConstructorReferenceOf(client, value, ctx),
                    resolved.genericEnv,
                    $"{resolved.classTypeInfo.classId}.{field.schemaKey}",
                    // Deferred-ownership bookkeeping is for
                    // PrepareConstructedGraph's preflight, which only runs on
                    // the staged-graph path. Here the adoption already
                    // happened above, so the map is write-only.
                    new Dictionary<string, NeoValueOwnership>());
                field.value = CallSiteWritePayload(payload, stagedRows);
            }
        }

        /// <summary>
        /// Wraps a computed payload for the write path: the staged child rows
        /// travel in the envelope so the write publishes them alongside the row
        /// it mints. A payload with no child rows and no <c>classId</c> is
        /// passed through bare, so a scalar reaches the write target exactly as
        /// it does today.
        /// </summary>
        private static object? CallSiteWritePayload(
            object? payload,
            List<MemberValue> stagedRows)
        {
            object? value = payload;
            string? classId = null;
            if (payload is NeoValuePayload wrapped)
            {
                // Its rows are already in `stagedRows`:
                // ComputeRuntimeConstructorPayload appends them.
                value = wrapped.value;
                classId = wrapped.classId;
            }
            if (stagedRows.Count == 0 && classId is null) return value;
            return new NeoValuePayload(value, classId, stagedRows);
        }

        /// <summary>
        /// Resolves a generated value nested inside a supplied collection to
        /// the row the constructed instance will reference, adopting it into
        /// Session first. The import is what applies the ordinary ownership
        /// rules — a parentless Session row attaches as-is, an already-parented
        /// one is rejected by name, and a Save/Asset one is cloned — so a
        /// collection entry behaves exactly like a Class member assigned at the
        /// call site.
        /// </summary>
        private static NeoConstructorValueReference? ImportedConstructorReferenceOf(
            NeoClient client,
            object? value,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            NeoConstructorValueReference? source =
                NeoScript.NSGetterEvaluator.ConstructorReferenceOf(value, ctx);
            if (source is null) return null;
            if (string.IsNullOrEmpty(source.Value.valueId)) return source;
            string importedId = NeoScriptExecutor.ImportClassValueReference(
                client,
                NeoValueOwnership.Session,
                source.Value.valueId,
                ctx);
            return new NeoConstructorValueReference(
                importedId,
                NeoValueOwnership.Session);
        }

        private static object? MarshalDeclaredConstructorArgument(
            NeoClient client,
            NeoResolvedDeclaredConstructor resolved,
            NeoDeclaredConstructorArgument argument,
            NeoScript.NSGetterEvaluator.Context ctx)
        {
            ConstructorRecord? record = resolved.link.record;
            if (record is null)
            {
                throw new InvalidOperationException(
                    $"Declared constructor call on class '{resolved.schemaClass.name}' supplies argument '{argument.name}' but resolved to the implicit parameterless constructor.");
            }
            FunctionArgumentTypeInfo? declared = null;
            foreach (FunctionArgumentTypeInfo candidate in record.argumentTypes)
            {
                if (candidate.name != argument.name) continue;
                declared = candidate;
                break;
            }
            if (declared is null)
            {
                throw new InvalidOperationException(
                    $"Declared constructor '{record.id}' on class '{resolved.schemaClass.name}' does not declare argument '{argument.name}'.");
            }
            return NeoScriptValueMarshaller.Normalize(
                client,
                NeoValueOwnership.Session,
                argument.value,
                declared,
                ctx,
                $"argument '{argument.name}' of constructor '{record.id}'");
        }

        /// <summary>
        /// Validates all class/type-info/member metadata carried by constructor IR
        /// without inspecting argument values. The evaluator invokes this
        /// before evaluating any argument pointer, matching NeoScript's
        /// compile-time call-shape ordering and preventing stale IR from
        /// running argument side effects.
        /// </summary>
        internal static RuntimeConstructorMetadata
            ValidateRuntimeClassConstructorMetadata(
            NeoClient client,
            ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields)
        {
            AssertMemberWiseConstructionIsAvailable(
                client,
                classTypeInfo.classId);
            return ValidateRuntimeClassConstructorMetadataCore(
                client,
                classTypeInfo,
                fields);
        }

        private static RuntimeConstructorMetadata
            ValidateRuntimeClassConstructorMetadataCore(
                NeoClient client,
                ClassTypeInfo classTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            bool requireSuppliedRequiredFields = true)
        {
            ConstructorSchemaCache? constructorCache = null;
            ConstructorMetadataCacheKey cacheKey = default;
            bool cacheEmptyFieldMetadata = fields.Count == 0
                && classTypeInfo.typeArguments is null;
            if (cacheEmptyFieldMetadata)
            {
                constructorCache = ConstructorSchemaCaches.GetOrCreateValue(
                    client);
                cacheKey = new ConstructorMetadataCacheKey(
                    classTypeInfo,
                    requireSuppliedRequiredFields);
                lock (constructorCache.gate)
                {
                    if (constructorCache.emptyFieldMetadata.TryGetValue(
                            cacheKey,
                            out RuntimeConstructorMetadata? cached))
                    {
                        return cached;
                    }
                }
            }
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
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                ResolveRuntimeInstanceEnv(
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
            RuntimeClassPlan classPlan = ResolveRuntimeClassPlan(
                client,
                classTypeInfo.classId,
                genericEnv);
            IList<MergedSchemaEntry> schema = classPlan.schema;
            Dictionary<string, MergedSchemaEntry> schemaByKey =
                classPlan.schemaByKey;
            Dictionary<string, Member> membersBySchemaKey =
                classPlan.membersBySchemaKey;

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
                Member member = membersBySchemaKey[entry.schemaKey];
                if (!IsStoredConstructorMember(member)) continue;
                // A declared constructor never has to name every required
                // field at the call site — its body may set them — so this
                // check is off for that path and the finished instance is
                // checked instead (P43 §6.1).
                if (requireSuppliedRequiredFields
                    && RequiresRuntimeConstructorArgument(member)
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
            var metadata = new RuntimeConstructorMetadata
            {
                membersBySchemaKey = membersBySchemaKey,
                genericEnv = genericEnv,
                classPlan = classPlan,
            };
            if (constructorCache is not null)
            {
                lock (constructorCache.gate)
                {
                    constructorCache.emptyFieldMetadata[cacheKey] = metadata;
                }
            }
            return metadata;
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
                // P67 §6 — a defaulted variant member is settled, so it must
                // stop being demanded as a runtime constructor argument.
                VariantMember member => member.defaultValue is not null,
                _ => false,
            };
        }

        /// <summary>
        /// P43 §1 — the member's <b>computed</b> default, or null when its
        /// default is literal or absent. Shared with
        /// <see cref="MemberValueFactory.CreateFromDefault"/>, which fails
        /// closed on the same signal.
        /// </summary>
        private static InitializerBody? InitializerOf(Member schemaMember) =>
            MemberValueFactory.InitializerOf(schemaMember);

        /// <summary>
        /// P43 §1.1 — evaluates a computed default (or a computed value row)
        /// and stages its product exactly the way a supplied constructor field
        /// is staged, so an initializer that builds a class graph is attached
        /// through the ordinary import funnel rather than copied.
        /// </summary>
        private static string? MaterializeInitializedValue(
            NeoClient client,
            Member member,
            InitializerBody init,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path)
        {
            object? produced = scope.EvaluateInitializer(member, init);
            if (produced is not null
                && (member is ListMember || member is DictionaryMember))
            {
                // A collection-typed initializer that returned an existing row
                // (rather than a fresh literal) is attached by reference; the
                // enumerate-and-materialize fallback below would otherwise copy
                // the entries' ids into brand-new scalar rows.
                NeoConstructorValueReference? backing = scope.ValueReference(produced);
                if (backing is NeoConstructorValueReference reference
                    && !string.IsNullOrEmpty(reference.valueId))
                {
                    if (reference.ownership is NeoValueOwnership ownership)
                    {
                        scope.referenceOwnershipByPath[path] = ownership;
                    }
                    return reference.valueId;
                }
            }
            return MaterializeRuntimeConstructorValue(
                client,
                member,
                produced,
                rows,
                nowIso,
                scope.ValueReference,
                env,
                path,
                scope.referenceOwnershipByPath);
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

            string valueId = Guid.NewGuid().ToString();
            object? payload = ComputeRuntimeConstructorPayload(
                client,
                member,
                runtimeValue,
                rows,
                nowIso,
                valueReference,
                genericEnv,
                path,
                referenceOwnershipByPath);
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

        /// <summary>
        /// The <b>value</b> half of
        /// <see cref="MaterializeRuntimeConstructorValue"/>: turns one
        /// generated-C# constructor value into the payload its member's value
        /// row stores, appending every child row that payload references to
        /// <paramref name="rows"/>.
        ///
        /// <para>Split out from row creation because P49 §4.4's call-site
        /// initializer block does not mint a row — it writes through the target
        /// a <c>this.X = …</c> assignment resolves to, which owns the row's id
        /// and lifecycle. That path still needs the enum option ids, the lookup
        /// ids and the entry rows a List or Dictionary value expands into, so
        /// the computation is shared rather than reimplemented: the member-wise
        /// factory and the declared-constructor seam cannot drift on what a
        /// generated value means.</para>
        /// </summary>
        private static object? ComputeRuntimeConstructorPayload(
            NeoClient client,
            Member member,
            object? runtimeValue,
            List<MemberValue> rows,
            string nowIso,
            Func<object?, NeoConstructorValueReference?> valueReference,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            string path,
            Dictionary<string, NeoValueOwnership> referenceOwnershipByPath)
        {
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
            return payload;
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

        /// <param name="path">
        /// This row's ROOT-ANCHORED dotted path — the same
        /// <c>{root.classId}</c> / <c>{path}.{schemaKey}</c> /
        /// <c>{path}[{index}]</c> / <c>{path}[{key}]</c> scheme
        /// <see cref="PrepareConstructedGraph"/> builds. Every ownership key an
        /// initializer records has to be keyed this way or the preflight misses
        /// it and falls back to guessing the ownership from the client, which
        /// re-parents an existing shadowed row instead of cloning it.
        /// </param>
        private static ObjectMemberValue CreateWritableClassValueRow(
            NeoClient client,
            string classId,
            Dictionary<string, string>? providedValue,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            string path,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null,
            RuntimeClassPlan? classPlan = null)
        {
            if (!scope.classStack.Add(classId))
            {
                throw new InvalidOperationException(
                    $"Recursive default class value creation detected for class '{classId}'.");
            }
            try
            {
                var value = providedValue is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(providedValue);

                RuntimeClassPlan? resolvedClassPlan = classPlan
                    ?? (classArguments is null
                        ? ResolveRuntimeClassPlan(client, classId)
                        : null);
                IList<MergedSchemaEntry> mergedSchema = resolvedClassPlan?.schema
                    ?? ResolveMergedSchema(client, classId, classArguments);
                // Chain env overlaid with the owning slot's constructed
                // arguments (specs/class-generics.md §4.1) — an
                // instance of the declared open class binds its params
                // through the slot, not a named subclass's chain.
                IReadOnlyDictionary<string, NeoGenericEnvEntry> env =
                    resolvedClassPlan?.genericEnv
                    ?? ResolveRuntimeInstanceEnv(
                        client,
                        classId,
                        classArguments);
                foreach (var entry in mergedSchema)
                {
                    if (value.ContainsKey(entry.schemaKey)) continue;
                    Member? member = resolvedClassPlan is null
                        ? null
                        : resolvedClassPlan.membersBySchemaKey[entry.schemaKey];
                    if (member is null
                        && !client.TryGetMember(entry.memberId, out member))
                    {
                        throw new InvalidOperationException(
                            $"Class '{classId}' schema key '{entry.schemaKey}' references missing member '{entry.memberId}'.");
                    }
                    // Generic slots substitute to their binding before the
                    // required check and default construction — required and
                    // defaultValue travel with the binding
                    // (specs/class-generics.md Decision 10).
                    if (resolvedClassPlan is null)
                    {
                        member = NeoGenericResolution.SubstituteMember(
                            client,
                            member,
                            env);
                    }
                    if (!IsStoredConstructorMember(member)) continue;

                    // P43 §1 / §8 — an init-backed default is EVALUATED here
                    // rather than read, so a runtime-constructed instance gets
                    // what the initializer produces now instead of a value
                    // baked at the last push. It also materializes regardless
                    // of `required`: an initializer is an explicit statement
                    // that this member has a value, which is exactly the
                    // signal the required-only filter below is standing in for.
                    InitializerBody? init = InitializerOf(member);
                    if (init is not null)
                    {
                        string? initValueId = MaterializeInitializedValue(
                            client,
                            member,
                            init,
                            rows,
                            nowIso,
                            scope,
                            env,
                            $"{path}.{entry.schemaKey}");
                        if (initValueId is not null)
                        {
                            value[entry.schemaKey] = initValueId;
                        }
                        continue;
                    }

                    if (!member.required) continue;

                    var defaultRow = CreateDefaultValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        scope,
                        env,
                        $"{path}.{entry.schemaKey}");
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
                scope.classStack.Remove(classId);
            }
        }

        private static IList<MergedSchemaEntry> ResolveMergedSchema(
            NeoClient client,
            string classId,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null)
        {
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(
                client);
            if (classArguments is null)
            {
                lock (cache.gate)
                {
                    if (cache.mergedSchemas.TryGetValue(
                            classId,
                            out IList<MergedSchemaEntry>? cached))
                    {
                        return cached;
                    }
                }
            }
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
                ResolveRuntimeInstanceEnv(client, classId, classArguments));
            if (unboundParamId is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot create default class value for open generic class '{schemaClass.name}': generic param '{unboundParamId}' is unbound — every generic param must be bound before instantiation (specs/class-generics.md Decision 6).");
            }
            IList<MergedSchemaEntry> resolved =
                NeoSchemaClassInheritance.MergeInstanceSchema(
                NeoSchemaClassInheritance.ResolveChain(
                    classId,
                    id => client.TryGetClass(id, out NeoSchemaClass? match)
                        ? match
                        : null),
                id => client.TryGetMember(id, out Member? member)
                    ? member
                    : null);
            if (classArguments is null)
            {
                lock (cache.gate)
                {
                    cache.mergedSchemas[classId] = resolved;
                }
            }
            return resolved;
        }

        private static IReadOnlyDictionary<string, NeoGenericEnvEntry>
            ResolveRuntimeInstanceEnv(
            NeoClient client,
            string classId,
            IReadOnlyDictionary<string, GenericBinding>? classArguments)
        {
            if (classArguments is not null)
            {
                return NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    classId,
                    classArguments);
            }
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(
                client);
            lock (cache.gate)
            {
                if (cache.genericEnvironments.TryGetValue(
                        classId,
                        out IReadOnlyDictionary<string, NeoGenericEnvEntry>? cached))
                {
                    return cached;
                }
            }
            IReadOnlyDictionary<string, NeoGenericEnvEntry> resolved =
                NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    classId,
                    classArguments: null);
            lock (cache.gate)
            {
                cache.genericEnvironments[classId] = resolved;
            }
            return resolved;
        }

        private static RuntimeClassPlan ResolveRuntimeClassPlan(
            NeoClient client,
            string classId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>?
                knownGenericEnv = null)
        {
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(
                client);
            lock (cache.gate)
            {
                if (cache.classPlans.TryGetValue(
                        classId,
                        out RuntimeClassPlan? cached))
                {
                    return cached;
                }
            }

            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                knownGenericEnv ?? ResolveRuntimeInstanceEnv(
                    client,
                    classId,
                    classArguments: null);
            IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                client,
                classId);
            var schemaByKey = new Dictionary<string, MergedSchemaEntry>(
                schema.Count);
            var membersBySchemaKey = new Dictionary<string, Member>(schema.Count);
            foreach (MergedSchemaEntry entry in schema)
            {
                if (!schemaByKey.TryAdd(entry.schemaKey, entry))
                {
                    throw new InvalidOperationException(
                        $"Class constructor schema for '{classId}' contains duplicate merged key '{entry.schemaKey}'.");
                }
                if (!client.TryGetMember(entry.memberId, out Member? member))
                {
                    throw new InvalidOperationException(
                        $"Class constructor schema field '{entry.schemaKey}' references missing member '{entry.memberId}'.");
                }
                membersBySchemaKey[entry.schemaKey] =
                    NeoGenericResolution.SubstituteMember(
                        client,
                        member,
                        genericEnv);
            }
            var resolved = new RuntimeClassPlan
            {
                schema = schema,
                genericEnv = genericEnv,
                schemaByKey = schemaByKey,
                membersBySchemaKey = membersBySchemaKey,
                factoryMember = new ClassMember
                {
                    id = $"__neo_factory_class_{classId}",
                    name = "Factory",
                    kind = MemberKind.Class,
                    classId = classId,
                },
            };
            lock (cache.gate)
            {
                cache.classPlans[classId] = resolved;
            }
            return resolved;
        }

        /// <param name="path">
        /// The root-anchored path of the slot this default fills — see
        /// <see cref="CreateWritableClassValueRow"/>.
        /// </param>
        private static MemberValue? CreateDefaultValueRow(
            NeoClient client,
            Member schemaMember,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path)
        {
            if (InitializerOf(schemaMember) is not null)
            {
                // Every caller must route a computed default through
                // MaterializeInitializedValue; reading one as a literal would
                // silently produce "no value" (P43 §1.1).
                throw new InvalidOperationException(
                    $"Member '{schemaMember.name}' has a computed default and cannot be materialized as a literal.");
            }
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
                // P67 §6 — copied, not aliased, like every other
                // reference-typed default row above.
                case VariantMember member:
                    return member.defaultValue is null
                        ? null
                        : new VariantMemberValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = member.defaultValue.value is null
                                ? null
                                : new VariantRefValue
                                {
                                    classId = member.defaultValue.value.classId,
                                    variantId = member.defaultValue.value.variantId,
                                },
                            classId = member.defaultValue.classId,
                        };
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
                        scope,
                        path);
                case DictionaryMember member:
                    return CreateDefaultDictionaryValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        scope,
                        env,
                        path);
                case ListMember member:
                    return CreateDefaultListValueRow(
                        client,
                        member,
                        rows,
                        nowIso,
                        scope,
                        env,
                        path);
                default:
                    return null;
            }
        }

        private static ObjectMemberValue CreateDefaultClassValueRow(
            NeoClient client,
            ClassMember member,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            string path)
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
                scope,
                path,
                member.classArguments);
            return CreateWritableClassValueRow(
                client,
                effectiveClassId,
                provided,
                rows,
                nowIso,
                scope,
                path,
                member.classArguments);
        }

        private static Dictionary<string, string> CloneDefaultClassChildren(
            NeoClient client,
            Dictionary<string, string>? source,
            string classId,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            string path,
            IReadOnlyDictionary<string, GenericBinding>? classArguments = null,
            Dictionary<string, string>? clonedIdsBySourceId = null)
        {
            var result = new Dictionary<string, string>();
            if (source is null || source.Count == 0) return result;
            clonedIdsBySourceId ??= new Dictionary<string, string>(StringComparer.Ordinal);

            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            foreach (var entry in ResolveMergedSchema(client, classId, classArguments))
            {
                schemaByKey[entry.schemaKey] = entry;
            }
            var env = ResolveRuntimeInstanceEnv(
                client,
                classId,
                classArguments);

            foreach (var pair in source)
            {
                if (!schemaByKey.TryGetValue(pair.Key, out var entry)) continue;
                if (!client.TryGetMember(entry.memberId, out Member? member)) continue;
                if (!client.TryGetValue(pair.Value, out MemberValue? sourceRow)) continue;

                Member effectiveMember = NeoGenericResolution.SubstituteMember(
                    client,
                    member,
                    env);
                // P43 §1.1a — an init-backed ROW is evaluated, not copied. Its
                // id still names a stored, addressable row in the authored
                // graph; only its interior is computed.
                if (sourceRow.init is not null)
                {
                    string? initValueId = MaterializeInitializedValue(
                        client,
                        effectiveMember,
                        sourceRow.init,
                        rows,
                        nowIso,
                        scope,
                        env,
                        $"{path}.{pair.Key}");
                    if (initValueId is not null)
                    {
                        result[pair.Key] = initValueId;
                    }
                    continue;
                }

                var cloned = CloneStoredValueForMember(
                    client,
                    effectiveMember,
                    sourceRow,
                    rows,
                    nowIso,
                    scope,
                    env,
                    $"{path}.{pair.Key}",
                    clonedIdsBySourceId);
                if (cloned is null) continue;

                rows.Add(cloned);
                result[pair.Key] = cloned.id;
                clonedIdsBySourceId[sourceRow.id] = cloned.id;
            }
            return result;
        }

        /// <summary>
        /// Clones the effective schema surface of a stored Class row. A P75
        /// construction root may omit every constructor-settled key from its
        /// persisted body, so enumerating <c>source.value</c> loses exactly the
        /// defaults this path is responsible for reproducing.
        /// </summary>
        private static Dictionary<string, string> CloneEffectiveClassChildren(
            NeoClient client,
            ObjectMemberValue source,
            string classId,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            string path,
            IReadOnlyDictionary<string, GenericBinding>? classArguments,
            Dictionary<string, string> clonedIdsBySourceId)
        {
            var result = new Dictionary<string, string>();
            var env = ResolveRuntimeInstanceEnv(client, classId, classArguments);
            foreach (MergedSchemaEntry entry in ResolveMergedSchema(
                client,
                classId,
                classArguments))
            {
                if (!client.TryGetMember(entry.memberId, out Member? member)) continue;
                MemberValue? sourceRow = client.ResolveClassChildRow(
                    source,
                    entry.schemaKey);
                if (sourceRow is null || sourceRow.IsRemoved) continue;
                Member effectiveMember = NeoGenericResolution.SubstituteMember(
                    client,
                    member,
                    env);
                if (sourceRow.init is not null)
                {
                    string? initValueId = MaterializeInitializedValue(
                        client,
                        effectiveMember,
                        sourceRow.init,
                        rows,
                        nowIso,
                        scope,
                        env,
                        $"{path}.{entry.schemaKey}");
                    if (initValueId is not null)
                    {
                        result[entry.schemaKey] = initValueId;
                    }
                    continue;
                }

                MemberValue? cloned = CloneStoredValueForMember(
                    client,
                    effectiveMember,
                    sourceRow,
                    rows,
                    nowIso,
                    scope,
                    env,
                    $"{path}.{entry.schemaKey}",
                    clonedIdsBySourceId);
                if (cloned is null) continue;
                rows.Add(cloned);
                result[entry.schemaKey] = cloned.id;
                clonedIdsBySourceId[sourceRow.id] = cloned.id;
            }
            return result;
        }

        private static ObjectMemberValue? CreateDefaultDictionaryValueRow(
            NeoClient client,
            DictionaryMember member,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path)
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
                scope,
                env,
                path,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private static ArrayMemberValue? CreateDefaultListValueRow(
            NeoClient client,
            ListMember member,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path)
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
                scope,
                env,
                path,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private static MemberValue? CloneStoredValueForMember(
            NeoClient client,
            Member member,
            MemberValue source,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path,
            Dictionary<string, string> clonedIdsBySourceId)
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
                {
                    string classId = sourceValue.classId ?? classMember.classId;
                    ObjectMemberValue clone = CreateWritableClassValueRow(
                        client,
                        classId,
                        CloneEffectiveClassChildren(
                            client,
                            sourceValue,
                            classId,
                            rows,
                            nowIso,
                            scope,
                            path,
                            classMember.classArguments,
                            clonedIdsBySourceId),
                        rows,
                        nowIso,
                        scope,
                        path,
                        classMember.classArguments);
                    CopyDefaultConstructionProvenance(
                        client,
                        sourceValue,
                        clone,
                        clonedIdsBySourceId);
                    return clone;
                }
                case DictionaryMember dictionaryMember
                    when source is ObjectMemberValue sourceValue:
                    return CloneDictionaryValueRow(
                        client,
                        dictionaryMember,
                        sourceValue,
                        rows,
                        nowIso,
                        scope,
                        env,
                        path,
                        clonedIdsBySourceId);
                case ListMember listMember
                    when source is ArrayMemberValue sourceValue:
                    return CloneListValueRow(
                        client,
                        listMember,
                        sourceValue,
                        rows,
                        nowIso,
                        scope,
                        env,
                        path,
                        clonedIdsBySourceId);
                default:
                    return null;
            }
        }

        private static void CopyDefaultConstructionProvenance(
            NeoClient client,
            ObjectMemberValue source,
            ObjectMemberValue clone,
            IReadOnlyDictionary<string, string> clonedIdsBySourceId)
        {
            clone.genericBindings = source.genericBindings is null
                ? null
                : new Dictionary<string, string>(source.genericBindings);
            clone.instanceVariantId = source.instanceVariantId;
            clone.instanceVariantRowValueId = source.instanceVariantRowValueId;

            Dictionary<string, JToken?>? constructorArgs = null;
            if (source.constructorArgs is not null)
            {
                constructorArgs = new Dictionary<string, JToken?>(
                    StringComparer.Ordinal);
                foreach (KeyValuePair<string, JToken?> argument in source.constructorArgs)
                {
                    constructorArgs[argument.Key] = argument.Value?.DeepClone();
                }
            }
            if (source.instanceConstructorId is string constructorId
                && constructorArgs is not null
                && client.TryGetConstructor(
                    constructorId,
                    out ConstructorRecord? constructor))
            {
                for (int index = 0; index < constructor.argumentTypes.Length; index++)
                {
                    FunctionArgumentTypeInfo parameter = constructor.argumentTypes[index];
                    if (parameter.type is not (
                        MemberKind.Class
                        or MemberKind.Interface
                        or MemberKind.List
                        or MemberKind.Dictionary
                        or MemberKind.Generic))
                    {
                        continue;
                    }
                    string parameterId = NeoClient.ConstructorParameterId(
                        constructor,
                        index);
                    if (!constructorArgs.TryGetValue(parameterId, out JToken? token)
                        || token?.Type != JTokenType.String
                        || token.Value<string>() is not string sourceValueId
                        || !clonedIdsBySourceId.TryGetValue(
                            sourceValueId,
                            out string clonedValueId))
                    {
                        continue;
                    }
                    constructorArgs[parameterId] = new JValue(clonedValueId);
                }
            }
            if (!source.hasInstanceConstructorId)
            {
                clone.constructorArgs = constructorArgs;
                return;
            }
            if (constructorArgs is null)
            {
                clone.instanceConstructorId = source.instanceConstructorId;
                clone.constructorArgs = null;
                return;
            }
            NeoClient.StampConstructionProvenance(
                clone,
                source.instanceConstructorId,
                constructorArgs);
        }

        private static ObjectMemberValue CloneDictionaryValueRow(
            NeoClient client,
            DictionaryMember member,
            ObjectMemberValue source,
            List<MemberValue> rows,
            string nowIso,
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path,
            Dictionary<string, string> clonedIdsBySourceId)
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
                    // P43 §1.1a — a computed entry row evaluates; its literal
                    // siblings still clone.
                    if (sourceRow.init is not null)
                    {
                        string? initEntryId = MaterializeInitializedValue(
                            client,
                            entryMember,
                            sourceRow.init,
                            rows,
                            nowIso,
                            scope,
                            entryEnv,
                            $"{path}[{pair.Key}]");
                        if (initEntryId is null)
                        {
                            throw new InvalidOperationException(
                                $"Dictionary default for '{member.name}' key '{pair.Key}' has an initializer that produced no value.");
                        }
                        value[pair.Key] = initEntryId;
                        continue;
                    }
                    var cloned = CloneStoredValueForMember(
                        client,
                        entryMember,
                        sourceRow,
                        rows,
                        nowIso,
                        scope,
                        entryEnv,
                        $"{path}[{pair.Key}]",
                        clonedIdsBySourceId);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary default for '{member.name}' key '{pair.Key}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    value[pair.Key] = cloned.id;
                    clonedIdsBySourceId[sourceRow.id] = cloned.id;
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
            NeoConstructionScope scope,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string path,
            Dictionary<string, string> clonedIdsBySourceId)
        {
            // Same stamp semantics as CloneDictionaryValueRow.
            var entryEnv = source.genericBindings is null
                ? env
                : NeoGenericResolution.EnvFromStamp(source.genericBindings);
            string rowId = Guid.NewGuid().ToString();
            bool unordered = member.listKind == NeoListKinds.Unordered;
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
                IEnumerable<string> sourceIds = source.value;
                if (unordered
                    && client.ResolveEffectiveRow(source.id) is not null)
                {
                    sourceIds = client.GetUnorderedListEntryIds(source.id);
                }
                foreach (string sourceId in sourceIds)
                {
                    if (!client.TryGetValue(sourceId, out MemberValue? sourceRow))
                    {
                        throw new InvalidOperationException(
                            $"List default for '{member.name}' references missing value '{sourceId}'.");
                    }
                    // P43 §1.1a — the canonical row-level initializer case: a
                    // literal list whose entries are constructor calls. The
                    // list stays stored and addressable; each computed entry
                    // evaluates its own interior.
                    if (sourceRow.init is not null)
                    {
                        string? initEntryId = MaterializeInitializedValue(
                            client,
                            entryMember,
                            sourceRow.init,
                            rows,
                            nowIso,
                            scope,
                            entryEnv,
                            $"{path}[{value.Count}]");
                        if (initEntryId is null)
                        {
                            throw new InvalidOperationException(
                                $"List default for '{member.name}' has an entry initializer that produced no value.");
                        }
                        MemberValue? initialized = rows.Find(
                            candidate => candidate.id == initEntryId);
                        if (unordered && initialized is null)
                        {
                            throw new InvalidOperationException(
                                $"Unordered List default for '{member.name}' initializer returned unowned value '{initEntryId}'.");
                        }
                        if (unordered)
                        {
                            initialized!.containerId = rowId;
                        }
                        value.Add(initEntryId);
                        continue;
                    }
                    var cloned = CloneStoredValueForMember(
                        client,
                        entryMember,
                        sourceRow,
                        rows,
                        nowIso,
                        scope,
                        entryEnv,
                        $"{path}[{value.Count}]",
                        clonedIdsBySourceId);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"List default for '{member.name}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    if (unordered) cloned.containerId = rowId;
                    value.Add(cloned.id);
                    clonedIdsBySourceId[sourceRow.id] = cloned.id;
                }
            }

            var row = new ArrayMemberValue
            {
                id = rowId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = unordered ? Array.Empty<string>() : value.ToArray(),
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

        /// <summary>
        /// Resolves a required SpriteInfo runtime value. SpriteInfo.Empty is a
        /// present value whose Unity projection is null; every other null or
        /// unresolved value remains a hard contract error.
        /// </summary>
        public static Sprite? ReadRequiredSprite(
            NeoClient client,
            object? value,
            string unresolvedMessage)
        {
            var spriteValue = ToSpriteValue(value);
            if (NeoReadOnlySprite.IsEmptyValue(spriteValue)) return null;
            return NeoAssetResolver.ResolveSprite(client.assetDatabase, spriteValue)
                ?? throw new InvalidOperationException(unresolvedMessage);
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
                return fileId == null || sliceIndex == null
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
