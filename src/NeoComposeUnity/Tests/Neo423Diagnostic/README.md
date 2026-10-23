# Computed mailbox regression

`source.neo` is the authored input for `mailbox.json` and `Generated/`.
Exported normally with neo-compose commit `398f1058e812346a2d2d10354b89c4a07166052b`
through `sourceParityWorkspace` / `runExport`, namespace
`NeoCompose.Tests.Diagnostic423`, compiler revision 18.

The tests compare narrow generated subscriptions and explicit getters with
raw list contents through seeded transitions, clear/refill, nested reward
changes, removal/reinsertion, and subscription disposal. Any and explicit
loop bodies use the same direct scan; indexed scalar queries are separate.
