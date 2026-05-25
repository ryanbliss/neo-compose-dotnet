// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public interface INeoLocalizationFormatter
    {
        string Format(string value, IReadOnlyDictionary<string, object?>? arguments = null);
    }
}
