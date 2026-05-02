// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

// Tests reach into a few internal members (e.g.
// `NeoAttribute.parent`'s internal setter) so they can construct
// wrapper-tree shapes the production code path always builds via
// collection-type CreateChild. Both names cover the dual builds:
//   - `NeoCompose.Unity.Tests` — Unity's asmdef-driven build
//   - `NeoComposeUnity.Tests` — the IDE-shim csproj used by VSCode /
//     Rider when the monorepo is opened outside Unity
[assembly: InternalsVisibleTo("NeoCompose.Unity.Tests")]
[assembly: InternalsVisibleTo("NeoComposeUnity.Tests")]
