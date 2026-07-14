// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("6ff1b376-1b30-449d-8439-d822be95cec1")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
public enum NeoSmartTileCollider
{
    [NeoEnumOption("None", Text = "63c4fc38-4be9-4fdb-925c-5b7d3942fa21")]
    None,
    [NeoEnumOption("Sprite", Text = "9c77cfbc-049e-44c9-8dae-144ad8758c1b")]
    Sprite,
    [NeoEnumOption("Grid", Text = "0465f25d-046c-404a-b642-0bc9a52ca208")]
    Grid,
}
