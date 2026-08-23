#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P67 §7.2 — the runtime handle for one named configuration of a class.
    ///
    /// <para>Hand-written, following <see cref="NeoAnimationClip{T}"/> — and,
    /// like it, sharing the seeded system class's name. The seeded
    /// `NeoVariant&lt;TObject&gt;` class never emits as a generated C# class
    /// (P67 §1), so this handle is its only C# surface and there is no
    /// collision. There is no public constructor: a variant is obtained from a
    /// generated `Class.Variants` tree or from a `Variant`-kind member, never
    /// built by game code.</para>
    ///
    /// <para><b>Null means empty.</b> `Overrides` and `ChildOverrides` are
    /// nullable constructor parameters that settle to null when unauthored,
    /// and every read below treats null exactly as an empty partial and an
    /// empty list. There is no separate "unset" behaviour to reason about.</para>
    /// </summary>
    public sealed class NeoVariant<T> where T : NeoGeneratedClassValue
    {
        private readonly NeoClient client;

        /// <summary>
        /// Null for the reserved base entry (`&lt;Class&gt;.Variants.Base`),
        /// which denotes the class itself with no variant applied (§3.4).
        /// </summary>
        private readonly VariantRecord? record;
        private readonly NeoGeneratedClassValue? boundRow;

        internal NeoVariant(
            NeoClient client,
            string classId,
            VariantRecord? record,
            NeoGeneratedClassValue? boundRow = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(classId))
            {
                throw new ArgumentException(
                    "A variant handle cannot be built without a target class id.",
                    nameof(classId));
            }
            ClassId = classId;
            this.record = record;
            this.boundRow = boundRow;
        }

        /// <summary>
        /// The variant record's id, or null for the base entry. A `Variant`
        /// member stores exactly this alongside <see cref="ClassId"/>.
        /// </summary>
        public string? VariantId => record?.id;

        /// <summary>The target class this variant configures.</summary>
        public string ClassId { get; }

        /// <summary>
        /// The variant's authored name, or `"Base"` for the base entry.
        /// </summary>
        public string Name => record?.name ?? NeoVariantSupport.BaseVariantName;

        /// <summary>
        /// Resolved folder path (`"Trees/Oak"`), or null at the class root and
        /// for the base entry.
        /// </summary>
        public string? Folder => record?.folder;

        /// <summary>P68 §6 — the bound lookup row, when this is an erased lookup handle.</summary>
        internal string? RowValueId => boundRow?.valueId;

        /// <summary>
        /// Runs the construction path (P67 §4.1), in order: the `Initialize`
        /// closure, which returns a fresh instance; then `Overrides`, applied
        /// shallowly; then `ChildOverrides`, each resolving its selector
        /// against the new instance.
        ///
        /// <para>`Apply` deliberately does NOT run on this path.</para>
        /// </summary>
        public T Initialize()
        {
            return NeoVariantSupport.Materialize<T>(
                client,
                NeoVariantSupport.InitializeNode(
                    client,
                    ClassId,
                    record,
                    boundRow));
        }

        /// <summary>
        /// Runs the application path (P67 §4.2) for `ToVariant`, in order: the
        /// `Apply` closure if the variant declares one — a variant without it
        /// is declarative-only and simply skips this step — then writes the
        /// declarative halves through the sparse runtime overlay and persists
        /// the root provenance through which untouched values resolve.
        ///
        /// <para>Always in place. The value is <paramref name="source"/>, never
        /// a replacement instance.</para>
        /// </summary>
        internal T Apply(T source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            NeoVariantSupport.ApplyToNode(
                client,
                record,
                source.WritableBackingNode,
                source.ValueOwnership,
                boundRow);
            return source;
        }
    }

    /// <summary>
    /// P68 §7 — a named variant whose delegates receive a row from its bound
    /// collection. <see cref="Bind"/> erases that argument into a plain
    /// <see cref="NeoVariant{T}"/> handle for storage in a Variant member.
    /// </summary>
    public sealed class NeoLookupVariant<T, TValue>
        where T : NeoGeneratedClassValue
        where TValue : NeoGeneratedClassValue
    {
        private readonly NeoClient client;
        private readonly VariantRecord record;

        internal NeoLookupVariant(NeoClient client, VariantRecord record)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
        }

        public string VariantId => record.id;
        public string ClassId => record.classId;
        public string Name => record.name;
        public string? Folder => record.folder;

        public T Initialize(TValue value)
        {
            NeoVariantSupport.ValidateLookupRow(client, record, value);
            return NeoVariantSupport.Materialize<T>(
                client,
                NeoVariantSupport.InitializeNode(client, ClassId, record, value));
        }

        internal T Apply(T source, TValue value)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            NeoVariantSupport.ValidateLookupRow(client, record, value);
            NeoVariantSupport.ApplyToNode(
                client,
                record,
                source.WritableBackingNode,
                source.ValueOwnership,
                value);
            return source;
        }

        public NeoVariant<T> Bind(TValue value)
        {
            NeoVariantSupport.ValidateLookupRow(client, record, value);
            return new NeoVariant<T>(client, ClassId, record, value);
        }
    }

    /// <summary>
    /// P67 §6. What a `variant` IR pointer evaluates to: the stored
    /// `{classId, variantId}` pair, unresolved.
    ///
    /// <para>Deliberately not a <see cref="NeoVariant{T}"/>: the evaluator has
    /// no `T` to close the handle over, and the pair is all either intrinsic
    /// needs. A null <see cref="variantId"/> is the base selection.</para>
    /// </summary>
    internal sealed class NeoVariantReference
    {
        internal NeoVariantReference(
            string classId,
            string? variantId,
            string? rowValueId = null)
        {
            this.classId = classId;
            this.variantId = variantId;
            this.rowValueId = rowValueId;
        }

        internal string classId { get; }
        internal string? variantId { get; }
        internal string? rowValueId { get; }
    }

    /// <summary>
    /// A row-backed <see cref="NeoGeneratedClassValue"/> used only as the
    /// target of a variant's `ChildOverrides` compile.
    ///
    /// <para>`NeoAnimationCompiler.CompileChildOverrides` takes a generated
    /// value because a selector resolves against the target's `Children` and
    /// its provenance stamps. Generated wrappers are minted from classId-keyed
    /// factory tables that only generated code holds, so the evaluator — which
    /// has a row and no `T` — cannot produce one. This adapter wraps the same
    /// backing node the real wrapper would, which is the only thing the
    /// selector path reads.</para>
    /// </summary>
    internal sealed class NeoVariantTargetValue : NeoGeneratedClassValue
    {
        internal NeoVariantTargetValue(
            NeoClient client,
            NeoMemberClass node,
            NeoValueOwnership ownership)
            : base(
                client,
                node,
                node.value?.classId ?? string.Empty,
                isReadOnly: false,
                inheritedStorageOwnership: ownership)
        {
        }

        /// <summary>
        /// This wrapper is a borrowed view over a value the caller owns, and it
        /// is disposed as soon as one variant finishes applying. Releasing the
        /// backing value's clips here would stop the receiver's running
        /// animations and discard compiled definitions it is still playing —
        /// `ToVariant` writes members, it does not end the object's life.
        /// </summary>
        protected override bool OwnsBackingValueLifetime => false;
    }

    /// <summary>
    /// The non-generic half of <see cref="NeoVariant{T}"/>: everything that
    /// does not need `T`, plus the reflection seam that turns a value graph
    /// back into a generated `T`.
    /// </summary>
    internal static class NeoVariantSupport
    {
        /// <summary>P67 §3.4 — the reserved no-variant selection.</summary>
        internal const string BaseVariantName = "Base";

        private const string InitializeKey = "Initialize";
        private const string ApplyKey = "Apply";
        private const string OverridesKey = "Overrides";
        private const string ChildOverridesKey = "ChildOverrides";

        /// <summary>
        /// Per-`T` factory resolved once. Generated classes expose an
        /// `internal static CreateWritable(NeoClient, NeoMemberClassWritable)`
        /// returning themselves — the same method `Clone()` routes through.
        ///
        /// <para>Reflection rather than a lookup table because the registered
        /// factory table (<see cref="NeoClient.RegisterGeneratedClassFactories"/>)
        /// resolves an existing valueId row, whereas this has to wrap a node
        /// that was just built and has no registered row yet. A generic static
        /// cache gives one lookup per `T` for the lifetime of the domain, which
        /// keeps `ResolveVariant&lt;T&gt;(client, variantId)` to the two
        /// arguments §7.1 specifies.</para>
        /// </summary>
        private static class GeneratedFactory<TValue>
            where TValue : NeoGeneratedClassValue
        {
            internal static readonly MethodInfo? CreateWritable =
                typeof(TValue).GetMethod(
                    "CreateWritable",
                    // Generated factories are `internal static ... (NeoClient,
                    // NeoMemberClassWritable)`, so NonPublic is required and
                    // the parameter must be the WRITABLE node: the binder needs
                    // the declared type assignable FROM the supplied one, and
                    // NeoMemberClassWritable is not assignable from its base.
                    // Same flags as the NeoGenericBindings precedent.
                    BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                    binder: null,
                    types: new[] { typeof(NeoClient), typeof(NeoMemberClassWritable) },
                    modifiers: null);
        }

        internal static TValue Materialize<TValue>(
            NeoClient client,
            NeoMemberClassWritable node)
            where TValue : NeoGeneratedClassValue
        {
            MethodInfo? factory = GeneratedFactory<TValue>.CreateWritable;
            if (factory is null)
            {
                throw new InvalidOperationException(
                    $"Generated type '{typeof(TValue).FullName}' has no static CreateWritable(NeoClient, NeoMemberClassWritable), so a variant cannot produce one. Regenerate the project's C# types.");
            }
            object? created = factory.Invoke(null, new object?[] { client, node });
            if (created is TValue typed) return typed;
            throw new InvalidOperationException(
                $"Generated type '{typeof(TValue).FullName}'.CreateWritable returned '{created?.GetType().FullName ?? "null"}'. Regenerate the project's C# types.");
        }

        /// <summary>
        /// Resolves the record a `{classId, variantId}` pair names. A null
        /// <paramref name="variantId"/> is the base selection (§3.4) and
        /// resolves to a null record, not to a lookup failure.
        /// </summary>
        internal static VariantRecord? ResolveRecord(
            NeoClient client,
            string classId,
            string? variantId)
        {
            if (variantId is null) return null;
            if (!client.TryGetVariant(variantId, out VariantRecord? record))
            {
                throw new InvalidOperationException(
                    $"Variant '{variantId}' is not in this project export. Re-export the project, or regenerate the C# types if the variant was deleted.");
            }
            // The record's own classId is the authority on the target class
            // (§9); the pointer's is corroboration written at compile time.
            if (!string.IsNullOrEmpty(classId)
                && !string.Equals(record.classId, classId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' belongs to class '{record.classId}', but the reference names '{classId}'. Re-export the project.");
            }
            return record;
        }

        /// <summary>
        /// P67 §4.1 — the whole construction path, at node level so the typed
        /// handle and the evaluator share one implementation: the `Initialize`
        /// closure (or, for the base selection, the class's own declared
        /// construction), then `Overrides`, then `ChildOverrides`.
        /// </summary>
        internal static NeoMemberClassWritable InitializeNode(
            NeoClient client,
            string classId,
            VariantRecord? record,
            object? lookupRow = null,
            string? lookupRowValueId = null)
        {
            NeoMemberClassWritable node;
            if (record is null)
            {
                if (lookupRow is not null || lookupRowValueId is not null)
                {
                    throw new InvalidOperationException(
                        "The Base variant is not collection-bound and cannot receive a row.");
                }
                // Base selection: the class's own construction. Required
                // constructor parameters must be settleable, exactly as §6 says.
                node = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    classId,
                    constructorId: null,
                    arguments: Array.Empty<NeoDeclaredConstructorArgument>());
            }
            else
            {
                NeoMemberClass graph = ResolveGraph(client, record);
                if (!graph.TryGet(InitializeKey, out NeoMemberDelegate? initialize))
                {
                    throw new InvalidOperationException(
                        $"Variant '{DescribeVariant(record)}' has no Initialize delegate on its value graph '{record.valueId}'.");
                }
                object?[] arguments = LookupArguments(
                    client,
                    record,
                    lookupRow,
                    lookupRowValueId);
                node = RequireConstructedNode(
                    client,
                    initialize.Invoke(arguments),
                    record);
            }
            // `Apply` deliberately does not run on this path (§4.1).
            ApplyDeclarativeHalves(client, record, node, NeoValueOwnership.Session);
            return node;
        }

        /// <summary>
        /// P67 §4.2 + P75 §6 — the application path, in place. The root first
        /// records the incoming variant so its declarative layer exists at the
        /// stable virtual ids, then shadows answered by that layer are cleared.
        /// The imperative `Apply` closure runs last and pins only what it
        /// actually mutates.
        ///
        /// <para>The base selection applies nothing. It names the class itself,
        /// and "become the plain class again" is not a state a written value
        /// can be walked back to; returning the receiver untouched keeps
        /// `ToVariant` total.</para>
        /// </summary>
        internal static void ApplyToNode(
            NeoClient client,
            VariantRecord? record,
            NeoMemberClassWritable node,
            NeoValueOwnership ownership,
            object? lookupRow = null,
            string? lookupRowValueId = null)
        {
            if (record is null)
            {
                if (lookupRow is not null || lookupRowValueId is not null)
                {
                    throw new InvalidOperationException(
                        "The Base variant is not collection-bound and cannot receive a row.");
                }
                return;
            }
            object?[] arguments = LookupArguments(
                client,
                record,
                lookupRow,
                lookupRowValueId);
            client.StampVirtualInstanceVariant(
                node,
                ownership,
                record.id,
                lookupRowValueId);
            foreach (NeoAnimationCompiledWrite answered in CompileDeclarativeHalves(
                client,
                record,
                node,
                ownership,
                resolveSelectorsImmediately: true))
            {
                answered.ClearInstanceOverride();
            }
            client.RefreshVirtualInstanceVariant(node, ownership);
            RunApplyClosure(client, record, node, ownership, arguments);
        }

        private static void RunApplyClosure(
            NeoClient client,
            VariantRecord record,
            NeoMemberClass node,
            NeoValueOwnership ownership,
            object?[] lookupArguments)
        {
            NeoMemberClass graph = ResolveGraph(client, record);
            // Absent Apply is declarative-only application (§4.2 step 1), not
            // an error and not a fallback to Initialize — the draft's
            // reconstruct-the-instance mode is deliberately gone.
            //
            // `TryGet` alone is not the test: it succeeds for any member the
            // CLASS declares, and `Apply` is declared nullable on every variant.
            // "Unauthored" is the value being absent, which is what null means
            // here and what settles to "skip".
            if (!graph.TryGet(ApplyKey, out NeoMemberDelegate? apply)) return;
            if (apply.value?.value is null && apply.member.defaultValue is null) return;
            string? sourceValueId = node.overrideValueId ?? node.value?.id;
            if (string.IsNullOrEmpty(sourceValueId))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' cannot apply to an instance with no backing value id.");
            }
            // Explicit receiver ownership, not the delegate's own: the closure
            // lives in an Immutable variant graph while its target is a Save or
            // Session instance, so `Invoke(thisValueId)` — which resolves the
            // receiver in the DELEGATE's store — would look for it in assets
            // and fail. Same reason animation selectors use this overload.
            apply.InvokeValueReference(
                sourceValueId!,
                ownership,
                lookupArguments);
        }

        internal static void ValidateLookupRow(
            NeoClient client,
            VariantRecord record,
            NeoGeneratedClassValue row)
        {
            if (row is null) throw new ArgumentNullException(nameof(row));
            string? rowValueId = row.valueId;
            if (string.IsNullOrWhiteSpace(rowValueId))
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' requires a materialized collection row.");
            }
            ValidateLookupRowId(client, record, rowValueId!);
        }

        internal static void ValidateLookupRowId(
            NeoClient client,
            VariantRecord record,
            string rowValueId)
        {
            VariantFolderBinding binding = ResolveLookupBinding(client, record)
                ?? throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' is not collection-bound and cannot receive a row.");
            if (!client.TryGetMember(
                    binding.collectionMemberId,
                    out Member? collectionMember)
                || collectionMember is not ListMember)
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' binds missing List member '{binding.collectionMemberId}'. Re-export the project.");
            }
            if (!client.TryGetValue(
                    binding.collectionValueId,
                    out MemberValue? collectionValue))
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' binds missing collection value '{binding.collectionValueId}'. Re-export the project.");
            }
            bool ordered = collectionValue is ArrayMemberValue array
                && array.value is not null
                && Array.IndexOf(array.value, rowValueId) >= 0;
            bool unordered = client.TryGetValue(rowValueId, out MemberValue? row)
                && string.Equals(
                    row.containerId,
                    binding.collectionValueId,
                    StringComparison.Ordinal);
            if (!ordered && !unordered)
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' row '{rowValueId}' is not an entry of collection '{binding.collectionValueId}'.");
            }
        }

        private static object?[] LookupArguments(
            NeoClient client,
            VariantRecord record,
            object? lookupRow,
            string? explicitRowValueId)
        {
            VariantFolderBinding? binding = ResolveLookupBinding(client, record);
            if (binding is null)
            {
                if (lookupRow is not null || explicitRowValueId is not null)
                {
                    throw new InvalidOperationException(
                        $"Variant '{DescribeVariant(record)}' is not collection-bound and cannot receive a row.");
                }
                return Array.Empty<object?>();
            }
            if (lookupRow is null)
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' requires a row from collection '{binding.collectionValueId}'.");
            }
            string? rowValueId = explicitRowValueId
                ?? (lookupRow is INeoValueReference reference
                    ? reference.valueId
                    : null);
            if (string.IsNullOrWhiteSpace(rowValueId))
            {
                throw new InvalidOperationException(
                    $"Lookup variant '{DescribeVariant(record)}' received a value with no backing row.");
            }
            ValidateLookupRowId(client, record, rowValueId!);
            return new[] { lookupRow };
        }

        private static VariantFolderBinding? ResolveLookupBinding(
            NeoClient client,
            VariantRecord record)
        {
            foreach (VariantFolderRecord folder in client.variantFolders.Values)
            {
                if (!string.Equals(folder.classId, record.classId, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(folder.path, record.folder, StringComparison.Ordinal))
                    continue;
                return folder.binding;
            }
            return null;
        }

        /// <summary>
        /// Steps 2 and 3 of both paths: `Overrides` shallowly, then each
        /// `ChildOverrides` row against the instance. Shared because the two
        /// paths differ only in step 1.
        /// </summary>
        private static void ApplyDeclarativeHalves(
            NeoClient client,
            VariantRecord? record,
            NeoMemberClassWritable node,
            NeoValueOwnership ownership)
        {
            if (record is null) return;
            try
            {
                ApplyDeclarativeHalvesCore(client, record, node, ownership);
            }
            catch (InvalidOperationException error)
            {
                // The Overrides/ChildOverrides machinery is shared with
                // animation, so its messages say "Animation clip '<key>'
                // frame 0". Reframe the error: an author reading this failure is
                // editing a variant, and there is no clip involved.
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' could not apply its overrides: {error.Message}",
                    error);
            }
        }

        private static void ApplyDeclarativeHalvesCore(
            NeoClient client,
            VariantRecord record,
            NeoMemberClassWritable node,
            NeoValueOwnership ownership)
        {
            foreach (NeoAnimationCompiledWrite write in CompileDeclarativeHalves(
                client,
                record,
                node,
                ownership,
                resolveSelectorsImmediately: false))
            {
                write.Apply();
            }
        }

        private static IReadOnlyList<NeoAnimationCompiledWrite> CompileDeclarativeHalves(
            NeoClient client,
            VariantRecord record,
            NeoMemberClassWritable node,
            NeoValueOwnership ownership,
            bool resolveSelectorsImmediately)
        {
            using var instance = new NeoVariantTargetValue(client, node, ownership);
            NeoMemberClass graph = ResolveGraph(client, record);
            string variantKey = $"variant:{record.id}";
            var writes = new List<NeoAnimationCompiledWrite>();
            // Null Overrides settles to an empty partial: TryGet failing and a
            // row with no value are the same answer.
            if (graph.TryGet(OverridesKey, out NeoMemberClass? overrides)
                && overrides.value is not null)
            {
                NeoAnimationCompiler.FlattenOverrides(
                    client,
                    instance.BackingNode,
                    overrides,
                    Array.Empty<string>(),
                    instance.ValueOwnership,
                    writes,
                    variantKey,
                    0);
            }
            var selectorActions = new List<Action>();
            // Null ChildOverrides settles to an empty list, for the same reason.
            if (graph.TryGet(ChildOverridesKey, out NeoMemberList? childOverrides))
            {
                NeoAnimationCompiler.CompileChildOverrides(
                    instance,
                    childOverrides,
                    writes,
                    selectorActions,
                    variantKey,
                    0,
                    resolveSelectorsImmediately);
            }
            foreach (Action selectorAction in selectorActions) selectorAction();
            return writes;
        }

        private static NeoMemberClass ResolveGraph(
            NeoClient client,
            VariantRecord record)
        {
            if (string.IsNullOrEmpty(record.valueId))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' has no value graph.");
            }
            if (!client.TryGetValue(record.valueId, out ObjectMemberValue? row))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' points at value '{record.valueId}', which has no object row in this export.");
            }
            // The graph's own row carries its class. A CLI-pushed variant root
            // is a structural stored construction — a materialized `value` map
            // plus `constructorArgs`, never an `init` — so it reads through the
            // ordinary node machinery with no special case.
            string graphClassId = row.classId ?? string.Empty;
            if (graphClassId.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' root value '{record.valueId}' carries no classId.");
            }
            var factoryMember = new ClassMember
            {
                id = $"__neo_variant_{record.id}",
                name = "Variant",
                kind = MemberKind.Class,
                classId = graphClassId,
                createdAt = row.createdAt,
                updatedAt = row.updatedAt,
            };
            NeoValueOwnership ownership =
                client.TryGetValueOwnership(record.valueId, out NeoValueOwnership resolved)
                    ? resolved
                    : NeoValueOwnership.Asset;
            return new NeoMemberClass(client, factoryMember, record.valueId, ownership);
        }

        private static NeoMemberClassWritable RequireConstructedNode(
            NeoClient client,
            object? produced,
            VariantRecord record)
        {
            if (produced is null)
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' Initialize returned null; it must return a fresh {record.classId} instance.");
            }
            if (produced is NeoGeneratedClassValue generated)
            {
                return generated.WritableBackingNode;
            }
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Session);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            NeoConstructorValueReference? reference =
                NSGetterEvaluator.ConstructorReferenceOf(produced, ctx);
            if (reference is null || string.IsNullOrEmpty(reference.Value.valueId))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' Initialize returned a value with no backing row; it must return a constructed {record.classId} instance.");
            }
            string producedValueId = reference.Value.valueId;
            if (!client.TryGetValue(producedValueId, out ObjectMemberValue? row))
            {
                throw new InvalidOperationException(
                    $"Variant '{DescribeVariant(record)}' Initialize produced value '{producedValueId}', which has no object row.");
            }
            var factoryMember = new ClassMember
            {
                id = $"__neo_variant_instance_{record.id}",
                name = "VariantInstance",
                kind = MemberKind.Class,
                classId = row.classId ?? record.classId,
                createdAt = row.createdAt,
                updatedAt = row.updatedAt,
            };
            return new NeoMemberClassWritable(
                client,
                factoryMember,
                producedValueId,
                NeoValueOwnership.Session);
        }

        private static string DescribeVariant(VariantRecord record)
        {
            string folder = string.IsNullOrEmpty(record.folder)
                ? string.Empty
                : $"{record.folder}/";
            return $"{folder}{record.name}";
        }
    }
}
