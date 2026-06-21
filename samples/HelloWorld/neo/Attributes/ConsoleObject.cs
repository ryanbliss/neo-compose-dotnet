// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("060620d4-9f8b-417a-ba43-d6f010fc6edc")]
public abstract class ConsoleObject : NeoObject
{
    [NeoString("cd315e41-810f-4be5-8537-5e5cc6218976", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""ConsoleObject""}")]
    public string Name { get; init; }
}
