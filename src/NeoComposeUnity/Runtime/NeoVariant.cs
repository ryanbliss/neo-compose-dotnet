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

        internal NeoVariant(NeoClient client, string classId, VariantRecord? record)
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
                NeoVariantSupport.InitializeNode(client, ClassId, record));
        }

        /// <summary>
        /// Runs the application path (P67 §4.2) for `ToVariant`, in order: the
        /// `Apply` closure if the variant declares one — a variant without it
        /// is declarative-only and simply skips this step; then `Overrides`;
        /// then `ChildOverrides`.
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
                source.ValueOwnership);
            return source;
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
        internal NeoVariantReference(string classId, string? variantId)
        {
            this.classId = classId;
            this.variantId = variantId;
        }

        internal string classId { get; }
        internal string? variantId { get; }
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
        /// Per-`T` factory resolved once. Generated classes expose a static
        /// `CreateWritable(NeoClient, NeoMemberClass)` returning themselves —
        /// the same method `Clone()` routes through — but nothing hands the
        /// runtime a classId-keyed factory table (generated code passes its own
        /// tables into <see cref="NeoGeneratedTypesSupport.ResolveClassValue"/>
        /// at each call site instead). A generic static cache gives one
        /// reflection lookup per `T` for the lifetime of the domain, which is
        /// the cheapest correct option that keeps
        /// `ResolveVariant&lt;T&gt;(client, variantId)` to the two arguments
        /// §7.1 specifies.
        /// </summary>
        private static class GeneratedFactory<TValue>
            where TValue : NeoGeneratedClassValue
        {
            internal static readonly MethodInfo? CreateWritable =
                typeof(TValue).GetMethod(
                    "CreateWritable",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                    binder: null,
                    types: new[] { typeof(NeoClient), typeof(NeoMemberClass) },
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
                    $"Generated type '{typeof(TValue).FullName}' has no static CreateWritable(NeoClient, NeoMemberClass), so a variant cannot produce one. Regenerate the project's C# types.");
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
            VariantRecord? record)
        {
            NeoMemberClassWritable node;
            if (record is null)
            {
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
                node = RequireConstructedNode(client, initialize.Invoke(), record);
            }
            // `Apply` deliberately does not run on this path (§4.1).
            ApplyDeclarativeHalves(client, record, node, NeoValueOwnership.Session);
            return node;
        }

        /// <summary>
        /// P67 §4.2 — the whole application path, in place: the `Apply` closure
        /// when the variant declares one, then `Overrides`, then
        /// `ChildOverrides`.
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
            NeoValueOwnership ownership)
        {
            if (record is null) return;
            RunApplyClosure(client, record, node, ownership);
            ApplyDeclarativeHalves(client, record, node, ownership);
        }

        private static void RunApplyClosure(
            NeoClient client,
            VariantRecord record,
            NeoMemberClass node,
            NeoValueOwnership ownership)
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
            apply.InvokeValueReference(sourceValueId!, ownership);
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
                    0);
            }
            foreach (NeoAnimationCompiledWrite write in writes) write.Apply();
            foreach (Action selectorAction in selectorActions) selectorAction();
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
