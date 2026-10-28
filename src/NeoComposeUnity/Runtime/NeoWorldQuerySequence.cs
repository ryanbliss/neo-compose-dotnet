// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    // The iterator retains row identities, never a getter frame or a rented
    // argument buffer. Each step unwraps against the consuming context.
    internal sealed class NeoWorldQuerySequence : NSGetterEvaluator.ScriptSequence
    {
        private readonly IEnumerable<string> source;

        internal NeoWorldQuerySequence(NeoClient client, TypeInfo element, IEnumerable<string> source)
            : base(client, element) => this.source = source;

        protected override NSGetterEvaluator.ScriptCursor CreateCursor(NSGetterEvaluator.Context ctx) =>
            new Cursor(Owner, ElementType, source.GetEnumerator());

        private sealed class Cursor : NSGetterEvaluator.ScriptCursor
        {
            private IEnumerator<string>? source;

            internal Cursor(NeoClient client, TypeInfo element, IEnumerator<string> source)
                : base(client, element) => this.source = source;

            protected override bool Advance(NSGetterEvaluator.Context ctx, out object? value)
            {
                Owner.CommitScriptWritesForGrid();
                using var liveRows = Owner.ReadLiveRows();
                while (source!.MoveNext())
                {
                    string id = source.Current;
                    NeoValueNode? node = null;
                    if (Owner.ReadValue(id, ref node) is not { IsRemoved: false } row)
                        continue;
                    var ownership = Owner.TryGetValueOwnership(id, ref node, out var found) ? found : NeoValueOwnership.Asset;
                    value = NSGetterEvaluator.UnwrapRow(row, ctx, ownership, node);
                    if (ElementType is ClassTypeInfo type
                        && type.classId is not ("system_f671535d-0eb6-4139-9caf-5c5276febd31" or "system_d7fa161b-b269-4bf1-9ec9-3aebec67e6e0" or "system_61b30a92-90dc-4bf8-8503-ee4f6414effc")
                        && !NSGetterEvaluator.RuntimeClassExtends(value, type.classId, ctx))
                        continue;
                    return true;
                }
                value = null;
                return false;
            }

            protected override void Release()
            {
                source?.Dispose();
                source = null;
            }
        }
    }
}
