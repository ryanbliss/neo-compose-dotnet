// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    internal static class NeoLayerLinkRuntime
    {
        internal const string TrySpawnId = "system_fdeabfed-3e78-55ca-b793-3a5e3e5cdad7";
        internal const string TrySetId = "system_5728d83c-cd44-5c7d-a43f-61bf83ca2b91";
        internal const string TrySpawnChildId = "system_767acaf4-4f8c-5fdf-b562-a2e910ad489e";
        private static readonly object ResultSite = new();

        internal static bool TryInvoke(string memberId, object? receiver, object?[] args,
            NSGetterEvaluator.Context ctx, out object? result)
        {
            result = null;
            if (memberId != TrySpawnId && memberId != TrySetId && memberId != TrySpawnChildId)
                return false;
            var cell = NeoGeneratedTypesSupport.ReadVector2IntValue(args[0])
                ?? throw new NSGetterRuntimeError("Placement cell must be a Vector2Int.");
            ctx.client.CommitScriptWritesForGrid();
            NeoGeneratedClassValue link = View(receiver, ctx);
            NeoGeneratedClassValue entry = View(args[1], ctx);
            NeoPlacementOutcome placed = memberId == TrySpawnChildId
                ? ctx.client.TrySpawnChild(link, cell, entry)
                : ctx.client.TryPlaceLayerEntry(link, cell, entry, memberId == TrySetId);
            result = ConstructResult(placed, ctx);
            return true;
        }

        internal static NeoGeneratedClassValue View(object? value, NSGetterEvaluator.Context ctx)
        {
            if (value is NeoGeneratedClassValue generated)
                return generated;
            if (value is NeoScriptObject detached)
                return new PlacementView(ctx.client, detached);
            string? id = NSGetterEvaluator.FindRowIdByReference(value, ctx)
                ?? (value as INeoValueReference)?.valueId;
            if (id is null)
                throw new NSGetterRuntimeError("Layer placement requires a class value with an identity.");
            return ctx.client.ResolveRegisteredGeneratedClassValue(id,
                NSGetterEvaluator.FindRowOwnershipByReference(value, ctx),
                (client, node, readOnly) => new PlacementView(client, node))
                ?? throw new NSGetterRuntimeError($"Layer placement class '{id}' could not be resolved.");
        }

        private sealed class PlacementView : NeoGeneratedClassValue
        {
            internal PlacementView(NeoClient client, NeoMemberClass node)
                : base(client, node, node.ClassId, node.ownership == NeoValueOwnership.Asset) { }
            internal PlacementView(NeoClient client, NeoDetachedValue value)
                : base(client, value, isReadOnly: false) { }
        }

        internal static object ConstructResult(NeoPlacementOutcome result, NSGetterEvaluator.Context ctx)
        {
            if (!NeoGeneratedTypesSupport.TryGetResolvedSite(ctx.client, ResultSite,
                out NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved))
            {
                resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(ctx.client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "system_8244dd57-5f81-569e-96b8-6a4d46dc5e09" },
                    "system_62070d17-25e9-5cca-96a6-da2e97cb15aa", new[] { "ok", "errorCode", "message" },
                    Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
                NeoGeneratedTypesSupport.CacheResolvedSite(ctx.client, ResultSite, resolved);
            }
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = result.Ok;
            arguments[resolved.argumentPositions[1]] = result.ErrorCode;
            arguments[resolved.argumentPositions[2]] = result.Message;
            return NSGetterEvaluator.ConstructDeclared(resolved, arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx,
                evaluateFieldValues: null, replayContext: false)!;
        }
    }
}
