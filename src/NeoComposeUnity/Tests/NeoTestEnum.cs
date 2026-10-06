// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A generated-shape Neo enum that declares no members, so every option id
    /// a test hands it becomes an undeclared value. Runtime seams that only
    /// know a value by type see exactly what a generated enum gives them.
    /// </summary>
    [NeoEnum(typeof(NeoTestEnumOptions))]
    internal enum NeoTestEnum
    {
    }

    internal static class NeoTestEnumOptions
    {
        public static NeoTestEnum FromOptionId(string optionId)
        {
            return NeoUndeclaredEnumOptions<NeoTestEnum>.FromOptionId(optionId);
        }

        public static string OptionId(this NeoTestEnum value)
        {
            return NeoUndeclaredEnumOptions<NeoTestEnum>.OptionId(value);
        }
    }
}
