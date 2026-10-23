# Narrowed Variant setter regression

`source.neo`, `variant-overrides.json`, and `Generated/` exercise required,
optional, same-target, and descendant-target Variant overrides through real
generated C# wrappers. The original four source-layer acceptance assertions
are preserved, including rejection of a base selection through the base view
of a narrowed instance.

The fixture was normally exported at compiler revision 18 from the combined
neo-compose producer headed at `9dd0e4a688958240b907504bc68c97a219d1d9ad`,
integrating `398f1058e812346a2d2d10354b89c4a07166052b` and
`34988c89476465dd8331f0160daba4fdda361a9a`.
Producer staged-diff SHA256:
`fae94f3735765aaee16c48c6eb9f3303b75229e60f240c94fcd098012cd87194`.
Export SHA256:
`7b8cf10192bb04ce4cab931fbf04e4e0012e2357ee3317c00df528fb009c66d3`.
No compiled-body or revision stamps were edited.
