// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// Tests construct providers against fake sockets/clocks through internal
// constructors and read internal diagnostics (e.g. the JWT provider's
// last-failure classification). The editor assembly builds its facade on the
// provider's internal raw-subscription seam.
//
// Each consumer is named twice: once under its Unity asmdef name (the
// assembly Unity compiles) and once under its IDE-shim csproj name (the
// assembly the IDE / `dotnet` compiles from NeoComposeConvex*.csproj), so
// internal access resolves in both builds. Naming an assembly that doesn't
// exist in a given build is a harmless no-op. Mirrors the runtime SDK's
// AssemblyInfo (NeoCompose.Unity.Tests + NeoComposeUnity.Tests).
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Tests")]
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Editor")]
[assembly: InternalsVisibleTo("NeoComposeConvex.Tests")]
[assembly: InternalsVisibleTo("NeoComposeConvexEditor")]
