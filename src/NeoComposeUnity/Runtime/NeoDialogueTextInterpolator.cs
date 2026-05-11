// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Text;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    internal static class NeoDialogueTextInterpolator
    {
        private const string VariableTokenPrefix = "{{neo-var:";
        private const string VariableTokenSuffix = "}}";

        internal static string Interpolate(
            NeoClient client,
            string text,
            Dictionary<string, DialogueTextVariable>? variables,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore,
            string ownerKind,
            string ownerId)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(VariableTokenPrefix, System.StringComparison.Ordinal) < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                int tokenStart = text.IndexOf(VariableTokenPrefix, index, System.StringComparison.Ordinal);
                if (tokenStart < 0)
                {
                    builder.Append(text, index, text.Length - index);
                    break;
                }

                int idStart = tokenStart + VariableTokenPrefix.Length;
                int tokenEnd = text.IndexOf(VariableTokenSuffix, idStart, System.StringComparison.Ordinal);
                if (tokenEnd < 0)
                {
                    builder.Append(text, index, text.Length - index);
                    break;
                }

                string variableId = text.Substring(idStart, tokenEnd - idStart);
                builder.Append(text, index, tokenStart - index);
                builder.Append(EvaluateVariable(
                    client,
                    variables,
                    variableId,
                    dialogueContext,
                    memoryStore,
                    ownerKind,
                    ownerId));
                index = tokenEnd + VariableTokenSuffix.Length;
            }

            return builder.ToString();
        }

        private static string EvaluateVariable(
            NeoClient client,
            Dictionary<string, DialogueTextVariable>? variables,
            string variableId,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore,
            string ownerKind,
            string ownerId)
        {
            if (string.IsNullOrEmpty(variableId)
                || variables == null
                || !variables.TryGetValue(variableId, out DialogueTextVariable variable))
            {
                throw new KeyNotFoundException(
                    $"Dialogue {ownerKind} '{ownerId}' references missing text variable '{variableId}'.");
            }

            if (variable.getter == null)
            {
                throw new NSGetterRuntimeError(
                    $"Dialogue text variable '{variableId}' on {ownerKind} '{ownerId}' has no compiled getter.");
            }

            var ctx = NeoDialogueConditionEvaluator.BuildContext(
                client,
                dialogueContext,
                memoryStore);
            object? result = NSGetterEvaluator.Evaluate(variable.getter, ctx);
            if (result is string text) return text;
            if (result is null)
            {
                throw new NSGetterRuntimeError(
                    $"Dialogue text variable '{variableId}' on {ownerKind} '{ownerId}' returned null; expected string.");
            }
            throw new NSGetterRuntimeError(
                $"Dialogue text variable '{variableId}' on {ownerKind} '{ownerId}' returned {ResultTypeName(result)}; expected string.");
        }

        private static string ResultTypeName(object value)
        {
            if (value is bool) return "bool";
            if (value is string) return "string";
            if (value is double || value is float || value is int || value is long) return "number";
            if (value is object?[]) return "list";
            if (value is IDictionary<string, object?>) return "object";
            return value.GetType().Name;
        }
    }
}
