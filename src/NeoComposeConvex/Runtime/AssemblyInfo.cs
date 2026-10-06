// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// Tests construct providers against fake sockets/clocks through internal
// constructors and read internal diagnostics (e.g. the JWT provider's
// last-failure classification).
//
// The tests are named twice: once under their Unity asmdef name (the
// assembly Unity compiles) and once under their IDE-shim csproj name (the
// assembly the IDE / `dotnet` compiles from NeoComposeConvex.Tests.csproj), so
// internal access resolves in both builds. Naming an assembly that doesn't
// exist in a given build is a harmless no-op. Mirrors the runtime SDK's
// AssemblyInfo (NeoCompose.Unity.Tests + NeoComposeUnity.Tests).
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Tests")]
[assembly: InternalsVisibleTo("NeoComposeConvex.Tests")]
