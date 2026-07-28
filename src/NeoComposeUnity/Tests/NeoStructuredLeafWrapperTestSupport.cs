// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Minimal stand-in for a generated read-only class value, shared by the
    /// vector, color, and sprite wrapper fixtures.
    ///
    /// <para>P42 decision D5: a bound wrapper's field setter must refuse to
    /// write when the generated value that handed it out is read-only.
    /// <c>NeoGeneratedClassValue.writableNode</c> is materialized without
    /// consulting <see cref="NeoGeneratedClassValue.IsReadOnly"/>, so the
    /// node alone cannot answer that question — the wrapper has to be told.
    /// Real generated wrappers are codegen output and far too heavy to build
    /// in a unit test; all the guard needs from an owner is its
    /// <c>IsReadOnly</c> flag.</para>
    /// </summary>
    internal sealed class NeoReadOnlyClassValueDouble : NeoGeneratedClassValue
    {
        public NeoReadOnlyClassValueDouble(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId)
            : base(client, node, fallbackClassId, isReadOnly: true) { }
    }
}
