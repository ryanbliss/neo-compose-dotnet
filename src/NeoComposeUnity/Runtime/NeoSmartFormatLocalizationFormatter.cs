// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using UnityEngine.Localization.SmartFormat;

namespace NeoCompose.Runtime
{
    public sealed class NeoSmartFormatLocalizationFormatter : INeoLocalizationFormatter
    {
        private readonly SmartFormatter formatter;

        public NeoSmartFormatLocalizationFormatter()
            : this(Smart.CreateDefaultSmartFormat())
        {
        }

        public NeoSmartFormatLocalizationFormatter(SmartFormatter formatter)
        {
            this.formatter = formatter;
        }

        public string Format(string value, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            return arguments == null
                ? formatter.Format(value)
                : formatter.Format(value, arguments);
        }
    }
}
