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
            NeoDialogueContext dialogueContext)
        {
            if (conditions == null || conditions.Length == 0) return true;
            foreach (var condition in conditions)
            {
                if (!Evaluate(client, condition, dialogueContext)) return false;
            }
            return true;
        }

        private static bool Evaluate(
            NeoClient client,
            LogicCondition condition,
            NeoDialogueContext dialogueContext)
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

            var ctx = BuildContext(client, dialogueContext);
            var result = NSGetterEvaluator.Evaluate(getter, ctx);
            if (result is bool b) return b;
            throw new NSGetterRuntimeError(
                $"Dialogue condition returned {ResultTypeName(result)}; expected bool.");
        }

        internal static NSGetterEvaluator.Context BuildContext(
            NeoClient client,
            NeoDialogueContext dialogueContext)
        {
            var ctx = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            object? rootValue = ResolveRootValue(client, ctx);
            object contextValue = new Dictionary<string, object?>
            {
                ["primary"] = dialogueContext.primary,
                ["trigger"] = dialogueContext.trigger,
            };
            return ctx
                .WithRoot(rootValue)
                .WithThis(dialogueContext.primary)
                .WithContext(contextValue);
        }

        private static object? ResolveRootValue(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            var root = new Dictionary<string, object?>(2)
            {
                ["assets"] = client.assets.value is ObjectAttributeValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx)
                    : null,
                ["save"] = client.save.value is ObjectAttributeValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx)
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
