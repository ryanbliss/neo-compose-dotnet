// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NeoCompose.Runtime
{
    public static class NeoDigitShapesOptions
    {
        public static DigitShapes FromOptionId(string optionId)
        {
            return optionId switch
            {
                "system_36613ff4-3187-4fd7-9776-d8139d33eac1" => DigitShapes.Context,
                "system_cc801d45-ee9f-4833-b453-f98546d4408d" => DigitShapes.None,
                "system_7d84702e-5457-41c7-87ba-b4492b044f18" => DigitShapes.NativeNational,
                _ => NeoUndeclaredEnumOptions<DigitShapes>.FromOptionId(optionId),
            };
        }

        public static string OptionId(this DigitShapes value)
        {
            return value switch
            {
                DigitShapes.Context => "system_36613ff4-3187-4fd7-9776-d8139d33eac1",
                DigitShapes.None => "system_cc801d45-ee9f-4833-b453-f98546d4408d",
                DigitShapes.NativeNational => "system_7d84702e-5457-41c7-87ba-b4492b044f18",
                _ => NeoUndeclaredEnumOptions<DigitShapes>.OptionId(value),
            };
        }

        public static string[] ToOptionIds(IEnumerable<DigitShapes>? options)
        {
            if (options is null)
                return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var option in options)
                ids.Add(option.OptionId());
            return ids.ToArray();
        }

        public static bool IsKnown(string optionId)
        {
            return optionId switch
            {
                "system_36613ff4-3187-4fd7-9776-d8139d33eac1" => true,
                "system_cc801d45-ee9f-4833-b453-f98546d4408d" => true,
                "system_7d84702e-5457-41c7-87ba-b4492b044f18" => true,
                _ => false,
            };
        }

        public static string TextId(this DigitShapes value)
        {
            return value switch
            {
                DigitShapes.Context => "Context",
                DigitShapes.None => "None",
                DigitShapes.NativeNational => "NativeNational",
                _ => value.OptionId(),
            };
        }

        public static string Text(this DigitShapes value, NeoClient? client = null)
        {
            if (client is null)
                throw new ArgumentNullException(nameof(client));
            return client.Localization.ResolveText(value.TextId());
        }
    }

}
