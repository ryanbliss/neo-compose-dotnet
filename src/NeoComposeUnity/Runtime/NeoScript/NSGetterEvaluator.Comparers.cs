// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Hashing;
using NeoCompose.Runtime.NeoScript.Numerics;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        // The schema lookup is the sole bridge to comparer class/member ids.
        // NeoSystemComparerClasses owns the minted class and member ids.
        private sealed class ComparerMetadata
        {
            internal readonly Dictionary<string, NeoSchemaClass> Classes = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, (string Owner, string Operation)> Operations = new(StringComparer.Ordinal);
            internal ComparerMetadata(Context ctx)
            {
                foreach (var declaration in ctx.client.ProjectDataForRuntime.classes.Values)
                {
                    string? owner = NeoSystemComparerClasses.NameOf(declaration.id);
                    if (owner is null)
                        continue;
                    Classes.Add(owner, declaration);
                }
                foreach (var pair in NeoSystemComparerClasses.Operations)
                    Operations[pair.Key] = pair.Value;
                foreach (string name in new[] { "IEqualityComparer`1", "IEqualityComparer", "IComparer`1", "IComparer" })
                    if (ctx.client.ProjectDataForRuntime.interfaces.TryGetValue(NeoPrimitiveConformance.InterfaceId(name), out var declaration))
                        foreach (var member in declaration.members)
                            Operations[member.Value.id!] = (name, member.Key + (name.EndsWith("`1", StringComparison.Ordinal) ? "" : "Object"));
            }
        }

        internal sealed class NeoBuiltInComparer
        {
            internal readonly NeoBuiltInComparerKind Kind;
            internal readonly NeoComparerFamilyKind Family;
            internal readonly TypeBinding? Type;
            internal readonly NeoDelegateValue[] Delegates;
            internal NeoBuiltInComparer(NeoBuiltInComparerKind kind, NeoComparerFamilyKind family, TypeBinding? type, NeoDelegateValue[]? delegates = null)
            {
                Kind = kind;
                Family = family;
                Type = type;
                Delegates = delegates ?? Array.Empty<NeoDelegateValue>();
            }
            internal JObject StoredPayload()
            {
                JObject payload = Payload();
                NeoStorablePayloads.ValidateBuiltInComparer(payload, "Stored comparer");
                return payload;
            }
            internal JObject Payload()
            {
                var payload = new JObject { ["comparer"] = (int)Kind, ["family"] = (int)Family };
                if (Type is not null)
                    payload["type"] = JObject.FromObject(Type);
                if (Delegates.Length != 0)
                    payload["delegates"] = JArray.FromObject(Delegates.Select(value => value.PersistedCopy()));
                return payload;
            }
        }

        private static NeoBuiltInComparer DefaultComparer(TypeBinding type, NeoComparerFamilyKind family, Context ctx)
        {
            var cache = ComparerCache(ctx);
            type = NeoInterfaceDispatch.Identity(type);
            var key = (type, family);
            if (!cache.BuiltIns.TryGetValue(key, out var value))
                cache.BuiltIns[key] = value = new NeoBuiltInComparer(NeoBuiltInComparerKind.Default, family, type);
            return value;
        }

        private static NeoBuiltInComparer SetComparer(TypeBinding type, Context ctx)
        {
            type = NeoInterfaceDispatch.Identity(type);
            var cache = ComparerCache(ctx).SetComparers;
            if (!cache.TryGetValue(type, out var comparer))
                cache[type] = comparer = new NeoBuiltInComparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, type);
            return comparer;
        }

        internal static NeoBuiltInComparer ReadBuiltInComparer(JObject payload, Context ctx)
        {
            var (kind, family) = NeoStorablePayloads.ValidateBuiltInComparer(payload, "Stored comparer");
            if (kind == NeoBuiltInComparerKind.ReferenceEquality)
                return ComparerCache(ctx).ReferenceComparer;
            TypeBinding type = TypeBindings.Read((JObject)payload["type"]!, "Stored comparer type");
            if (kind == NeoBuiltInComparerKind.Default)
                return DefaultComparer(type, family, ctx);
            if (kind == NeoBuiltInComparerKind.SetEquality)
                return SetComparer(type, ctx);
            var delegates = payload["delegates"]?.ToObject<NeoDelegateValue[]>();
            return new NeoBuiltInComparer(kind, family, type, delegates);
        }

        internal static NeoBuiltInComparer? UnwrapComparer(object? value) => value as NeoBuiltInComparer ?? (value as NeoBox)?.Payload as NeoBuiltInComparer;

        internal static bool TryComparerPointer(CallFunctionPointer pointer, object? receiver, NeoScriptScope scope, Context ctx, out object? result)
        {
            result = null;
            if (UnwrapComparer(receiver) is null)
                return false;
            string? memberId = pointer.interfaceMember?.memberId ?? pointer.memberId;
            var metadata = ComparerCache(ctx).Metadata ??= new ComparerMetadata(ctx);
            if (memberId is null || !metadata.Operations.ContainsKey(memberId))
                return false;
            var args = RentArguments(pointer);
            try
            {
                for (int i = 0; i < args.Length; i++)
                    args[i] = EvalPointer(pointer.args[i], scope, ctx);
                return TryComparerCall(memberId, receiver, args, ctx, null, out result);
            }
            finally
            {
                ReturnArguments(pointer, args);
            }
        }

        private static bool TryComparerCall(string memberId, object? receiver, object?[] args, Context ctx, TypeBinding? construction, out object? result)
        {
            result = null;
            var metadata = ComparerCache(ctx).Metadata ??= new ComparerMetadata(ctx);
            if (!metadata.Operations.TryGetValue(memberId, out var operation))
                return false;
            var comparer = UnwrapComparer(receiver);
            if (comparer is null)
            {
                if (operation.Operation == "GetInstance")
                {
                    result = ComparerCache(ctx).ReferenceComparer;
                    return true;
                }
                if (receiver is not null && operation.Operation.EndsWith("Object", StringComparison.Ordinal))
                {
                    var owner = (ComparerCache(ctx).Metadata!).Classes[operation.Owner];
                    var runtime = RuntimeTypeOfValue(receiver, null, ctx);
                    var element = NeoInterfaceDispatch.Of(ctx.client).ClassChain(runtime).First(item => item.Head!.ClassId == owner.id).Bindings!.Values.Single();
                    object? first = args[0], second = args.Length > 1 ? args[1] : null;
                    if (operation.Operation == "EqualsObject" && SameObject(first, second, ctx))
                    {
                        result = true;
                        return true;
                    }
                    if (first is null || args.Length > 1 && second is null)
                    {
                        result = operation.Operation == "CompareObject" ? (object)(double)(first is null ? second is null ? 0 : -1 : 1)
                            : operation.Operation == "GetHashCodeObject" ? 0d : false;
                        return true;
                    }
                    var target = NeoTypeBindings.ToTypeInfo(element);
                    var held = HeldType(first, null, ctx, out object payload);
                    if (!TryCast(first, held, payload, target, ctx, out first))
                        throw ComparerArgumentError();
                    if (args.Length > 1)
                    {
                        held = HeldType(second!, null, ctx, out payload);
                        if (!TryCast(second!, held, payload, target, ctx, out second))
                            throw ComparerArgumentError();
                    }
                    bool comparison = operation.Operation == "CompareObject";
                    string iface = NeoPrimitiveConformance.InterfaceId(comparison ? "IComparer`1" : "IEqualityComparer`1");
                    var declaration = ctx.client.ProjectDataForRuntime.interfaces[iface];
                    var closed = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: iface),
                        new Dictionary<string, TypeBinding> { [declaration.genericParams![0].id] = element });
                    string method = operation.Operation.Replace("Object", "");
                    result = InvokeMappedInterface(receiver, closed, declaration.members[method].id!, args.Length > 1 ? new[] { first, second } : new[] { first }, ctx);
                    return true;
                }
                if (operation.Operation is not ("GetDefault" or "Create"))
                    return false;
                if (construction?.Bindings is null)
                    throw new NSGetterRuntimeError("A generic comparer native requires its closed static construction.");
                var type = construction.Bindings.Values.Single();
                var family = operation.Owner == NeoSystemComparerClasses.EqualityComparer ? NeoComparerFamilyKind.Equality : NeoComparerFamilyKind.Comparison;
                if (operation.Operation == "GetDefault")
                    result = DefaultComparer(type, family, ctx);
                else
                {
                    if (args[0] is null)
                        throw NeoRuntimeError.ArgumentNull(family == NeoComparerFamilyKind.Equality ? "equals" : "comparison");
                    result = new NeoBuiltInComparer(NeoBuiltInComparerKind.Create, family, type, args.Where(value => value is not null).Cast<NeoDelegateValue>().ToArray());
                }
                return true;
            }
            object? x = args.Length > 0 ? args[0] : null, y = args.Length > 1 ? args[1] : null;
            string name = operation.Operation;
            if (name.EndsWith("Object", StringComparison.Ordinal) && comparer.Kind != NeoBuiltInComparerKind.ReferenceEquality)
            {
                if (name == "EqualsObject" && SameObject(x, y, ctx))
                {
                    result = true;
                    return true;
                }
                if (x is null || name != "GetHashCodeObject" && y is null)
                {
                    result = name == "CompareObject" ? (object)(double)(x is null ? y is null ? 0 : -1 : 1)
                        : name == "GetHashCodeObject" ? 0d : false;
                    return true;
                }
                TypeInfo target = NeoTypeBindings.ToTypeInfo(comparer.Type!);
                TypeBinding? held = HeldType(x, null, ctx, out object payload);
                if (!TryCast(x, held, payload, target, ctx, out x))
                    throw ComparerArgumentError();
                if (name != "GetHashCodeObject")
                {
                    held = HeldType(y!, null, ctx, out payload);
                    if (!TryCast(y!, held, payload, target, ctx, out y))
                        throw ComparerArgumentError();
                }
            }
            result = name switch
            {
                "Equals" or "EqualsObject" => BuiltInEquals(comparer, x, y, ctx),
                "GetHashCode" or "GetHashCodeObject" => (object)(double)BuiltInHash(comparer, x, ctx),
                "Compare" or "CompareObject" => (object)(double)BuiltInCompare(comparer, x, y, ctx),
                _ => throw new NSGetterRuntimeError($"Unknown built-in comparer operation '{name}'."),
            };
            return true;
        }

        private static NeoRuntimeError ComparerArgumentError() => new(NeoErrorClass.NeoArgumentError, "Type of argument is not compatible with the generic comparer.");

        internal static TypeBinding ComparerType(NeoBuiltInComparer comparer, Context ctx)
        {
            var metadata = ComparerCache(ctx).Metadata ??= new ComparerMetadata(ctx);
            string name = comparer.Kind == NeoBuiltInComparerKind.ReferenceEquality ? NeoSystemComparerClasses.ReferenceEqualityComparer
                : comparer.Family == NeoComparerFamilyKind.Equality ? NeoSystemComparerClasses.EqualityComparer : NeoSystemComparerClasses.Comparer;
            if (!metadata.Classes.TryGetValue(name, out var declaration))
                throw new NSGetterRuntimeError($"The schema has no system comparer class '{name}'.");
            var head = new TypeHead(MemberKind.Class, true, classId: declaration.id);
            if (comparer.Type is null)
                return TypeBinding.Closed(head);
            TypeBinding argument = comparer.Kind == NeoBuiltInComparerKind.SetEquality
                ? TypeBinding.Closed(new TypeHead(MemberKind.Set, true), new Dictionary<string, TypeBinding> { ["0"] = comparer.Type }) : comparer.Type;
            return TypeBinding.Closed(head, new Dictionary<string, TypeBinding> { [declaration.genericParams![0].id] = argument });
        }

        private static string ComparerName(NeoBuiltInComparer comparer, Context ctx) => comparer.Kind switch
        {
            NeoBuiltInComparerKind.ReferenceEquality => "ReferenceEqualityComparer",
            NeoBuiltInComparerKind.SetEquality => "HashSetEqualityComparer`1",
            NeoBuiltInComparerKind.Create => comparer.Family == NeoComparerFamilyKind.Equality ? "DelegateEqualityComparer`1" : "ComparisonComparer`1",
            _ => comparer.Family == NeoComparerFamilyKind.Equality ? SelectEquality(comparer.Type!, ctx).Name : SelectComparison(comparer.Type!, ctx).Name,
        };

        private static string ComparerText(NeoBuiltInComparer comparer, Context ctx)
        {
            string name = ComparerName(comparer, ctx);
            return "System.Collections.Generic." + name + (name.Contains('`') ? $"[{ClrTypeName(name.StartsWith("Nullable", StringComparison.Ordinal) ? comparer.Type!.WithRequired(true) : comparer.Kind == NeoBuiltInComparerKind.SetEquality ? TypeBinding.Closed(new TypeHead(MemberKind.Set, true), new Dictionary<string, TypeBinding> { ["0"] = comparer.Type! }) : comparer.Type!, ctx)}]" : "");
        }

        private static bool ComparerEquals(NeoBuiltInComparer x, NeoBuiltInComparer y, Context ctx)
        {
            if (x.Kind != y.Kind || x.Family != y.Family || !Equals(x.Type, y.Type))
                return false;
            if (x.Kind == NeoBuiltInComparerKind.Create)
                return x.Family == NeoComparerFamilyKind.Comparison ? ReferenceEquals(x, y)
                    : x.Delegates.Length == y.Delegates.Length && x.Delegates.Select((d, i) => DelegateEquals(d, y.Delegates[i], ctx)).All(equal => equal);
            return x.Kind != NeoBuiltInComparerKind.ReferenceEquality || ReferenceEquals(x, y);
        }

        private static int ComparerHash(NeoBuiltInComparer value, Context ctx)
        {
            if (value.Kind == NeoBuiltInComparerKind.ReferenceEquality || value.Kind == NeoBuiltInComparerKind.Create && value.Family == NeoComparerFamilyKind.Comparison)
                return IdentityHash(value, ctx);
            if (value.Kind == NeoBuiltInComparerKind.Create)
                return unchecked((17 * 31 + DelegateHash(value.Delegates[0], ctx)) * 31 + (value.Delegates.Length > 1 ? DelegateHash(value.Delegates[1], ctx) : 0));
            return NeoHashes.String(NeoTypeSources.Key(value.Type!)) ^ (int)value.Kind;
        }

        private static bool BuiltInEquals(NeoBuiltInComparer comparer, object? x, object? y, Context ctx)
        {
            switch (comparer.Kind)
            {
                case NeoBuiltInComparerKind.ReferenceEquality:
                    return SameObject(x, y, ctx);
                case NeoBuiltInComparerKind.Default:
                    return SelectEquality(comparer.Type!, ctx).Equals(x, y, ctx);
                case NeoBuiltInComparerKind.Create:
                    return JsTruthy(InvokeDelegate(comparer.Delegates[0], new[] { x, y }, ctx));
                default:
                    if (SameObject(x, y, ctx))
                        return true;
                    if (x is null || y is null)
                        return false;
                    x = RuntimeCollection(x, ctx);
                    y = RuntimeCollection(y, ctx);
                    var left = CollectionValues(x, ctx);
                    var right = CollectionValues(y, ctx);
                    object? xc = CollectionComparerValue(x!, comparer.Type!, ctx), yc = CollectionComparerValue(y!, comparer.Type!, ctx);
                    bool same = xc is NeoBuiltInComparer xb && yc is NeoBuiltInComparer yb ? ComparerEquals(xb, yb, ctx) : ObjectEquals(xc!, null, yc, ctx);
                    var equality = new BoundEquality(comparer.Type!, same ? xc : null, ctx);
                    if (same && left.Count != right.Count)
                        return false;
                    foreach (object? item in right)
                    {
                        if (same && x is RuntimeSet set)
                        {
                            if (!set.Contains(RuntimeSetKey(item, set, ctx)))
                                return false;
                        }
                        else if (same && TryStoredSetContains(x, item, new BoundEquality(comparer.Type!, null, ctx), ctx, out bool contains))
                        {
                            if (!contains)
                                return false;
                        }
                        else if (!left.Any(entry => equality.Equals(item, entry)))
                            return false;
                    }
                    return true;
            }
        }

        private static int BuiltInHash(NeoBuiltInComparer comparer, object? value, Context ctx)
        {
            switch (comparer.Kind)
            {
                case NeoBuiltInComparerKind.ReferenceEquality:
                    return IdentityHash(value, ctx);
                case NeoBuiltInComparerKind.Default:
                    return SelectEquality(comparer.Type!, ctx).Hash(value, ctx);
                case NeoBuiltInComparerKind.Create:
                    if (comparer.Delegates.Length < 2)
                        throw new NeoRuntimeError(NeoErrorClass.NeoNotSupportedError, "Specified method is not supported.");
                    return NeoInt32.Wrap(Number(InvokeDelegate(comparer.Delegates[1], new[] { value }, ctx)!));
                default:
                    if (value is null)
                        return 0;
                    int hash = 0;
                    var equality = SelectEquality(comparer.Type!, ctx);
                    foreach (object? entry in CollectionValues(value, ctx))
                        if (entry is not null)
                            hash ^= equality.Hash(entry, ctx);
                    return hash;
            }
        }

        private static int BuiltInCompare(NeoBuiltInComparer comparer, object? x, object? y, Context ctx) =>
            comparer.Kind == NeoBuiltInComparerKind.Create ? NeoInt32.Wrap(Number(InvokeDelegate(comparer.Delegates[0], new[] { x, y }, ctx)!))
                : SelectComparison(comparer.Type!, ctx).Compare(x, y, ctx);

        private static List<object?> CollectionValues(object? collection, Context ctx)
        {
            var values = new List<object?>();
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                while (cursor.MoveNextUnresolved())
                    values.Add(cursor.ResolveEntry(ctx));
            }
            catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
            finally { cursor.Dispose(); }
            return values;
        }

        private static object CollectionComparerValue(object collection, TypeBinding? fallbackType, Context ctx)
        {
            collection = RuntimeCollection(collection, ctx)!;
            if (collection is RuntimeSet { Equality: { } equality })
                return equality.Comparer ?? DefaultComparer(equality.Default.Type, NeoComparerFamilyKind.Equality, ctx);
            if (collection is RuntimeDictionary dictionary)
                return dictionary.Equality.Comparer ?? DefaultComparer(fallbackType ?? dictionary.Equality.Default.Type, NeoComparerFamilyKind.Equality, ctx);
            if (FindRowReference(collection, ctx) is { } reference && ctx.client.ReadValue(reference.ownership, reference.valueId, ref reference.node) is { } row)
            {
                TypeBinding entryType = fallbackType ?? (reference.CollectionMember(ctx.client) is DictionaryMember dictionaryMember
                    ? DictionaryKeyType(dictionaryMember) : NeoTypeBindings.OfMemberId(ctx.client, reference.EntryMember(ctx.client)!.id));
                var stored = StoredEquality(row, entryType, reference.ownership, ctx);
                return stored.Comparer ?? DefaultComparer(entryType, NeoComparerFamilyKind.Equality, ctx);
            }
            TypeBinding type = fallbackType ?? (CollectionEntryMember(collection, ctx) is { } member ? NeoTypeBindings.OfMemberId(ctx.client, member.id) : TypeBinding.Closed(new TypeHead(MemberKind.Object, false)));
            return DefaultComparer(type, NeoComparerFamilyKind.Equality, ctx);
        }

        internal static TypeBinding DictionaryKeyType(DictionaryMember member) => member.KeyKind == NeoDictionaryKeyKind.Enum
            ? TypeBinding.Closed(new TypeHead(MemberKind.Enum, true, enumId: member.keyEnumId))
            : TypeBinding.Closed(new TypeHead(MemberKind.String, true));

        internal static NeoCollectionComparer? WriteCollectionComparer(object? collection, NeoWritePlan? plan, NeoValueOwnership ownership, Context ctx, Func<object?, NeoConstructorValueReference?>? resolveReference = null, string collectionName = "collection")
        {
            var origin = collection is null ? null : FindRowReference(collection, ctx)
                ?? NeoGeneratedTypesSupport.ConstructorCollectionOrigin(collection);
            if (origin is { } source && ctx.client.ReadValue(source.ownership, source.valueId, ref source.node) is { comparer: { } descriptor })
            {
                if (descriptor.kind == NeoCollectionComparerKind.BuiltIn)
                    return new NeoCollectionComparer { kind = descriptor.kind, comparer = (JObject)descriptor.comparer!.DeepClone(), runtimeIdentity = descriptor.runtimeIdentity ?? descriptor };
                // Stored collections are values. Copy their owned comparer;
                // only a fresh local collection attaches its comparer instance.
                string copy = ctx.client.CloneOwnedValueReferenceForNewParent(ownership, source.ownership, descriptor.valueId!, null);
                return new NeoCollectionComparer { kind = descriptor.kind, valueId = copy };
            }
            BoundEquality? equality = collection switch
            {
                RuntimeSet set => set.Equality,
                RuntimeDictionary dictionary => dictionary.Equality,
                _ => null,
            };
            if (equality?.UsesDefault == true)
                return null;
            object? comparer = equality?.Comparer;
            if (UnwrapComparer(comparer) is { } builtIn)
                return new NeoCollectionComparer { kind = NeoCollectionComparerKind.BuiltIn, comparer = builtIn.StoredPayload(), runtimeIdentity = builtIn };
            if (comparer is not null)
            {
                string? sourceId = FindRowIdByReference(comparer, ctx) ?? ConstructorReferenceOf(comparer, ctx)?.valueId;
                if (sourceId is not null && ctx.client.TryGetValueOwnership(sourceId, out var sourceOwnership)
                    && ctx.client.TryFindOwnedParent(sourceOwnership, sourceId, out string? parentId))
                    throw new NSGetterRuntimeError($"Aggregate value '{sourceId}' is already owned by parent value '{parentId}' and cannot be attached to '{collectionName}.Comparer'. Call Clone() explicitly.");
                if (resolveReference is not null)
                    return new NeoCollectionComparer
                    {
                        kind = NeoCollectionComparerKind.Value,
                        valueId = resolveReference(comparer)?.valueId
                        ?? throw new NSGetterRuntimeError("An authored collection comparer requires a value identity.")
                    };
                string id = FindRowIdByReference(comparer, ctx) ?? ConstructorReferenceOf(comparer, ctx)?.valueId
                    ?? throw new NSGetterRuntimeError("An authored collection comparer requires a value identity.");
                string imported = plan is null ? NeoScriptExecutor.ImportClassValueReference(ctx.client, ownership, id, ctx)
                    : NeoScriptExecutor.ImportClassValueReference(plan, ctx.client, ownership, id, ctx);
                return new NeoCollectionComparer { kind = NeoCollectionComparerKind.Value, valueId = imported };
            }
            return null;
        }

        private static object? EvalCollectionComparer(CollectionComparerPointer pointer, NeoScriptScope scope, Context ctx)
        {
            TypeBinding? type = pointer.elementType is null ? null : RuntimeTypeOf(CloseContextType(pointer.elementType, ctx));
            if (pointer.op == "CreateSetComparer")
                return new NeoBuiltInComparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, type!);
            object? collection = EvalPointer(pointer.collection!, scope, ctx);
            if (collection is null)
            {
                if (pointer.optional == true)
                    return null;
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            }
            return CollectionComparerValue(collection, type, ctx);
        }
    }
}
