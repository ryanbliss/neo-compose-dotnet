// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// Tests construct providers against fake sockets/clocks through internal
// constructors and read internal diagnostics (e.g. the JWT provider's
// last-failure classification). The editor assembly builds its facade on the
// provider's internal raw-subscription seam.
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Tests")]
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Editor")]
