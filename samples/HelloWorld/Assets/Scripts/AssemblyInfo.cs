// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// The sample's EditMode tests drive internals (flare overflow, reboot flow)
// without widening the public surface.
[assembly: InternalsVisibleTo("Tests")]
[assembly: InternalsVisibleTo("HelloWorld.Tests.IDE")]
