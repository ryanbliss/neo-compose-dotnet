// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    internal static class NeoDialogueConditionEvaluator
    {
        internal static bool EvaluateAll(
            NeoClient client,
            LogicCondition[]? conditions,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore = null)
        {
            if (conditions == null || conditions.Length == 0) return true;
            foreach (var condition in conditions)
            {
                if (!Evaluate(client, condition, dialogueContext, memoryStore)) return false;
            }
            return true;
        }

        private static bool Evaluate(
            NeoClient client,
            LogicCondition condition,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore)
        {
            var getter = condition switch
            {
                UILogicCondition ui => ui.getter,
                CodeLogicCondition code => code.getter,
                _ => null,
            };
            if (getter == null)
            {
                throw new NSGetterRuntimeError("Dialogue condition has no compiled getter.");
            }

            var ctx = BuildContext(client, dialogueContext, memoryStore);
            var result = NSGetterEvaluator.Evaluate(getter, ctx);
            if (result is bool b) return b;
            throw new NSGetterRuntimeError(
                $"Dialogue condition returned {ResultTypeName(result)}; expected bool.");
        }

        internal static NSGetterEvaluator.Context BuildContext(
            NeoClient client,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore = null)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                memoryStore: memoryStore);
            object? rootValue = ResolveRootValue(client, ctx);
            object contextValue = new Dictionary<string, object?>
            {
                ["dialogueId"] = dialogueContext.DialogueId,
                ["groupId"] = dialogueContext.GroupId,
                ["nodeId"] = dialogueContext.NodeId,
                ["optionId"] = dialogueContext.OptionId,
                ["primary"] = dialogueContext.Primary,
                ["trigger"] = dialogueContext.Trigger,
                ["linkedValues"] = dialogueContext.LinkedValues,
            };
            return ctx
                .WithRoot(rootValue)
                .WithThis(dialogueContext.CurrentPrimary)
                .WithContext(contextValue);
        }

        private static object? ResolveRootValue(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            var root = new Dictionary<string, object?>(3)
            {
                ["Assets"] = client.assets.value is ObjectAttributeValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectAttributeValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectAttributeValue session
                    ? NSGetterEvaluator.UnwrapRow(session, ctx, NeoValueOwnership.Session)
                    : null,
            };
            return root;
        }

        private static string ResultTypeName(object? value)
        {
            if (value is null) return "null";
            if (value is bool) return "bool";
            if (value is string) return "string";
            if (value is double || value is float || value is int || value is long) return "number";
            if (value is object?[]) return "list";
            if (value is IDictionary<string, object?>) return "object";
            return value.GetType().Name;
        }
    }
}
