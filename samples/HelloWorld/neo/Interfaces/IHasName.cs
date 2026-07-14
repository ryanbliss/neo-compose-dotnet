// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoInterface("27d59731-311d-4d80-833a-916760c7ed30")]
public partial interface IHasName
{
    string Name { get; }
}
