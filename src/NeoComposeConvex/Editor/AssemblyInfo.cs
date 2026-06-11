// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// Tests drive the internal editor facade against the fake socket. Named under
// both the Unity asmdef name and the IDE-shim csproj name (NeoComposeConvex.Tests)
// so internal access resolves in the Unity and IDE builds alike.
[assembly: InternalsVisibleTo("NeoCompose.Unity.Convex.Tests")]
[assembly: InternalsVisibleTo("NeoComposeConvex.Tests")]
