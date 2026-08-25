# P75 deviations — Unity SDK

One acceptance criterion is met only in part. Everything else in P75
("virtual instance values") is implemented as specified.

## Acceptance criterion 3 — runtime construction is stamped, but not collapsed

**What the criterion asks for.** A runtime `new Thing(...)` or
`Variants.Fancy.Initialize()` should produce a row whose *unwritten* members
resolve through the virtual layer, exactly as a web-authored sparse instance
does — so a later change to a member's declared default reaches instances the
game created at runtime.

**What is implemented.**

- `ConstructDeclaredClassValue` stamps the canonical creation-provenance pair
  on the row it produces: an explicit `instanceConstructorId` (null for the
  implicit `new()`, which serializes) plus the evaluated `constructorArgs`.
  This is the seam behind both the generated-C# constructors and the
  evaluator's `new` expression, so every runtime-constructed row is now a
  durable P75 root.
- `NeoVariantSupport.InitializeNode` additionally records `instanceVariantId`
  (and the P68 lookup row) on a variant-constructed row, so it replays through
  the declarative layer it was built from rather than through the class's own
  construction.
- Both then expand through the ordinary `ExpandVirtualInstanceRoot` path, so
  any member the construction leaves absent resolves virtually, and later
  declaration changes reach it.

**What is not implemented: the collapse.** Construction *publishes* the whole
object graph before the stamp exists — `CreateSuppliedClassValue` mints a row
per member so the constructor body has a `this` to write to, and
`AssertDeclaredConstructorRootIsComplete` then checks that required members
were assigned. Every member therefore arrives materialized, and a materialized
row wins over its virtual counterpart. The stamp is durable and correct, but
the freshly constructed instance is not yet sparse: it becomes sparse only
after the collapse pass drops the rows that are equal to what a replay would
produce.

**Why the SDK cannot do the collapse alone.**

1. *The distinction is not observable at construction time.* "Assigned by the
   constructor body" and "left at the declared default" are the same write
   through the same member path. Telling them apart means re-deriving what a
   replay would produce and diffing — which is `ExpandVirtualInstanceRoot`
   run a second time over a graph that was just built, doubling the cost of
   every `new`.
2. *Removal is not local.* Dropping a member row mid-construction has to
   unlink it from the parent body, retarget the wrappers already handed to the
   constructor body, unwind the allocation tracker's registration, and leave
   `AssertDeclaredConstructorRootIsComplete` still able to prove required
   members were set. Each of those is a live invariant of P43/P49
   construction, not of P75.
3. *The web already owns this pass.* Collapse is defined as a push-time
   operation (the P74/P75 backwards flatten + collapse migration), and it runs
   against the authored corpus where the recipe and the produced graph are
   both available as data. Doing it a second way in the SDK would be a second
   implementation of the same rule, with its own drift.

**What would be needed to close it.** A collapse seam on the TypeScript side
that the SDK can share: a canonical "is this row equal to what the recipe
produces" predicate, applied at the same point on both runtimes. Given that,
the SDK's part is a post-construction sweep over the just-stamped root that
removes matching rows and lets the virtual index answer for them. Until that
seam exists, a runtime-constructed instance is a *stamped* root that becomes
sparse on its first push, rather than one that is born sparse.

**Practical consequence.** A runtime-constructed instance tracks declaration
changes from its first push onward, not from the moment of construction. An
instance the web authored is sparse immediately, as specified.

## Cross-runtime note: unordered list entry ids

A replayed unordered-list entry carries no authored-child provenance, so its
deterministic id falls back to the *positional* source identity
(`path:{memberId}:{pathKey}` with a `{"kind":"list","index":N}` segment). The
two runtimes therefore agree only while both walk the declared default in its
own order — the entry's own value id does not reach the derivation.
`P75VirtualInstanceValueTests.UnorderedListEntryIdsFollowTheDeclaredDefaultOrder`
pins the three literals so the TypeScript suite can assert the same strings.
