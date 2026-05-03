// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public interface INeoValuePayloadProvider
    {
        NeoValuePayload ToNeoValuePayload();
    }
}
