# Changelog

## [Unreleased]

### Changed

- Prepare NeoScript collection callback validation, return contracts, and
  parameter binding plans once per operator. Callback invocation now retains
  its parameter slots while still clearing body-local state (P58).

## [0.20.3] - 2026-08-10

### Changed

- Pre-size NeoScript `Where` and `Select` result builders from the source
  collection count, capped by the remaining P54 produced-entry budget, to
  reduce backing-store growth and copying without weakening resource limits
  (P59).

## [0.20.2] - 2026-08-09

### Changed

- Replace copied NeoScript callback scopes with parent-linked frames so callback setup scales with parameter count instead of captured caller bindings, while preserving lexical isolation and read-only diagnostics (P56).

## [0.20.1] - 2026-08-09

### Changed

- Stop `First`, `FirstOrDefault`, and collection `Contains` scans as soon as their result is known. Terminal scans resolve and charge only visited entries while preserving list/dictionary order and stored value-reference identity (P55).

### Fixed

- The first Save/Session write shadowing an authored NSDelegate row now
  clones the row (persisted-copying its payload) instead of throwing
  `Unsupported save value row type 'DelegateMemberValue'`.
- Shadowing a P42 `$partial` structured-leaf row into a Save/Session
  overlay now clones the row (deep-copying its envelope) instead of
  throwing `Unsupported save value row type 'PartialLeafMemberValue'`.

## [0.20.0] - 2026-08-07

### Added

- **NSAction multicast members (P62).** `MemberKind.NSAction` (ordinal 26)
  declares a void member holding an insertion-ordered set of member-target
  listeners. Invoking one fires every listener in stored order; an empty set
  is a successful no-op.
- `NeoAction` and `NeoAction<P1>` … `NeoAction<P1, …, P16>` registry classes
  with `AddListener` / `RemoveListener` (over `System.Action<…>` and
  `NeoDelegateValue`), `Invoke`, a read-only `Listeners` list, and
  `operator +` / `operator -` that perform the durable write and return the
  same instance. Subscriptions are ordinary member-value writes, deduplicated
  by `(memberId, valueId)` identity.
- `NeoMemberAction` member nodes with `Bind()` … `Bind<P1, …, P16>()`
  returning a reference-stable `NeoAction` per member node. A C# `Invoke`
  runs the same fan-out a NeoScript call runs: it supplies the owning row as
  the receiver for null-`valueId` listeners, and a throwing listener stops
  the invocation with `{action}[{owningRowId ?? "default"}] listener {index}
  threw: …`. Control faults (budget, deferred Function, native-unavailable)
  propagate as themselves rather than as listener errors.
- `NeoGeneratedTypesSupport.RequireSameAction(object? value,
  NeoActionBase expected, string memberLabel)` — the generated setter's
  identity check, which is pure reference identity against the action the
  member's own getter returns; `memberLabel` is error text only.
- `NeoGeneratedTypesSupport.ListenerTargetOf(Delegate listener,
  string? ownerValueId = null)`, plus the `[NeoMemberMethod]` attribute <!-- neo-terminology-audit: allow-line legacy-attribute-domain-word -- names the C# [NeoMemberMethod] attribute, not a Neo domain concept -->
  generated code uses to resolve a method group back to its declaring
  member. A listener whose receiver is the row that owns the action is
  stored with a null `valueId` — byte-identical to the identity NeoScript's
  `this.OnX += this.Handler` lowers to — so subscriptions, deduplication and
  removal are interchangeable across the two languages.
- `NeoActionValue` wire value (`{ listeners: [{ memberId, valueId }] }`), the
  `callAction` IR pointer, and the `addActionListener` /
  `removeActionListener` IR instructions (compiler revision 8).

### Changed

- **Breaking:** the project export schema version is now 17. Exports must be
  regenerated from a web app of the same release; the SDK enforces exact
  equality.

## [0.19.2] - 2026-08-07

### Fixed

- Trusted read-only projection no longer tries to materialize initializer-backed
  aggregate defaults as literal value rows before constructor evaluation.

## [0.19.1] - 2026-08-05

### Changed

- NeoScript constructor evaluation now budgets each materialized Session row
  and collection entry once when nested constructor graphs are republished by
  their ancestors.
- The default constructed-Session-row ceiling is 4,096, matching the web
  evaluator and supporting large authored preview graphs.

## [0.19.0] - 2026-08-03

### Added

- `NeoGeneratedTypesSupport.ReadRequiredSprite` preserves the distinction
  between the canonical non-null `SpriteInfo.Empty` value and a missing or
  unresolved required sprite asset.

### Changed

- Required sprite wrappers and generic bindings project `SpriteInfo.Empty` to
  no Unity `Sprite` without throwing. Non-empty unresolved required assets
  continue to fail closed.

## [0.18.0] - 2026-08-02

### Added

- **Initializer materialization (P61).** Materialized instance rows now
  preserve their evaluated `constructorArgs` creation data while the runtime
  continues to read the stored `value` graph as authority.

### Changed

- Row-level `init` is documented and validated as a declaration-graph shape;
  an exported instance carries `value`, its concrete `classId`, and optional
  `constructorArgs` instead of executable initializer code.

## [0.17.0] - 2026-08-02

### Added

- **First-class NeoScript delegates (P60).** The SDK now mirrors the
  `NSDelegate` wire member and callable type-info shapes, ships
  `NeoDelegate<TReturn, ...>` variants through 16 parameters, and exposes
  typed binding support through `NeoMemberDelegate`.
- Compiled `callDelegate` pointers execute inline closures with lexical
  `this`/`root` capture or dispatch bound native, NeoScript, and nested
  delegate member targets with cycle diagnostics.
- Animation tracks and child overrides resolve `Selector` delegates with the
  authored `OnLoad` or `PerFrame` refresh policy.

### Changed

- NeoScript compiler revision 7 is now supported for first-class delegate
  invocation IR. Older compiled bodies continue to run unchanged.

## [0.16.0] - 2026-07-31

### Added

- **NeoScript `for` and `foreach` loops (P50).** Compiled scripts can run
  C#-style `for` loops and `foreach (T item in Items)` loops, including
  `break` and `continue`. Collection membership is snapshotted at loop
  entry while values are read live, and every iteration participates in
  the evaluator's shared execution budget and resumable state machine.

- **NeoScript `switch` statements (P51).** Typed constant cases, nullable
  selectors, `default`, and switch-local `break` now execute with C#-style
  first-match semantics and no implicit fallthrough. Nested loops and
  switches route `break` to the nearest enclosing construct.

- **NeoScript `try`/`catch` blocks (P52).** Runtime failures can be handled
  as string messages by ordered `catch (string message)` clauses with
  optional `when` filters. An unmatched failure keeps propagating, while
  cancellation, execution-budget exhaustion, and malformed compiled IR
  remain non-catchable evaluator failures.

### Changed

- NeoScript compiler revision 6 is now supported. Revision 4 introduces
  loops, revision 5 introduces switches, and revision 6 introduces
  `try`/`catch`; older compiled bodies continue to run unchanged.

## [0.15.0] - 2026-07-31

### Added

- **Required constructors (P49 §1).** A class may declare its parameter
  list on the class header rather than as a member, and that list then
  becomes the class's only way in. `NeoSchemaClass.requiredConstructorId`
  carries the derived id on the wire. It is held apart from
  `constructorIds` because the two are mutually exclusive — a class that
  declares a required constructor declares no member constructors beside
  it — and because the required constructor is reached through that field
  alone, never through the ordered list.

  Load-time validation collects a class's ownership from both fields, so
  the required constructor's record is no longer swept up as disowned. It
  rejects an export whose class names a missing record, names a record
  another class owns, or declares a required constructor alongside member
  constructors.

  Base resolution consults both fields as well. A subclass of a class that
  declares a required constructor can now reach it — implicitly when the
  base takes no arguments, or by name from a base clause — where before the
  base looked as though it declared no constructors at all.

- **Base-clause initializer blocks (P49 §1.5).** A base clause may settle
  the base's members directly, `: Foo { Bar = bar }`, with or without
  arguments beside it; either half alone is a base clause, so this is the
  shape a base that declares no constructor is reached through.
  `ConstructorRecord.baseInitializerFields` carries the authored entries
  and `compiledBaseInitializerFields` the compiled getters at matching
  positions, evaluated in the declaring constructor's parameter scope.

  The block lands between the base chain and the declaring constructor's
  own body, so construction now runs: member initializers, then each base
  link followed by that link's base-clause block, then this constructor's
  body, then the call-site initializer block — which still wins. Where a
  base clause and a call site settle the same inherited member, the call
  site takes precedence: it is the one visible from where the author is
  standing. Every key in the block must name a stored member of the class
  under construction, and a stale key fails before any expression runs.

- **An `EvaluateDeclaredConstructor` overload that carries call-site
  values.** A generated constructor whose class has members its declared
  parameters do not cover appends those members as optional parameters and
  forwards them in a fifth argument:

  ```csharp
  public Enemy(int Health, NeoString? Name = null)
      : this(
          client,
          NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
              client,
              "class-enemy",
              "ctor-enemy",
              new global::NeoCompose.Runtime.NeoDeclaredConstructorArgument[]
              {
                  new global::NeoCompose.Runtime.NeoDeclaredConstructorArgument(
                      "Health", Health),
              },
              new global::NeoCompose.Runtime.NeoGeneratedConstructorValue[]
              {
                  new global::NeoCompose.Runtime.NeoGeneratedConstructorValue(
                      "Name", "member-enemy-name", Name),
              }),
          false,
          NeoValueOwnership.Session)
  {
  }
  ```

  The four-argument form is unchanged and delegates to the new one, so
  generated code that predates this release keeps compiling. The supplied
  values arrive as the call-site initializer block — step 4 — so a member
  supplied here refines whatever the member initializers and the
  constructor body wrote.

  The appended parameters accept the same value kinds the generated
  member-wise factory accepts, because the two now share one computation
  and cannot drift on what a generated value means: an enum member
  marshals to its option ids, and a List or Dictionary member expands into
  entry rows stamped with the class's generic bindings rather than being
  written verbatim. A Class value nested inside a supplied collection is
  adopted through the ordinary import funnel, so a parentless Session row
  attaches as-is, an already-parented row is rejected by name, and a Save
  or Asset row is cloned — the same rules a Class member assigned at the
  call site follows. A `null` for an appended optional parameter means
  *omitted*: the member keeps its initializer and its default, matching the
  generated `= null` and matching `CreateWritableClassValue`. That is the
  opposite of a `null` written in a NeoScript initializer block, which
  clears the member.

### Changed

- **A class that declares a required constructor cannot be constructed any
  other way (P49 §1.3).** The three paths that settle members without
  invoking a constructor now fail closed rather than publishing a
  half-settled instance: the implicit parameterless `new`, the generated
  member-wise factory, and the schema-derived `classConstructor` intrinsic
  that backs a `Foo { … }` construction expression in NeoScript. The error
  names the required constructor's parameters in declaration order.
  Settling a member is not the same as satisfying the constructor, and the
  header's parameter list is the statement that those values are not
  optional.

  The compiler rejects all three call sites too, so reaching one of these
  errors at runtime means stale generated code or stale compiled IR.
  Re-export the project and regenerate `NeoGeneratedTypes.cs`.

### Fixed

- A constructor record whose authored `code` is absent now loads.
  `ConstructorRecord.code` is nullable, and the absence is meaningful: a
  required constructor that declares no `init` block at all stores no
  source, which is indistinguishable at runtime from a block that was
  declared and left empty. The SDK executes the compiled `action` and never
  the source, so a truncated export is still caught — by the missing
  action rather than by the missing text.

## [0.14.0] - 2026-07-30

### Breaking

- **`NeoPlayDirection` is a wrapper class rather than a C# enum, and
  `Backward` is now `Reverse`.** A track row carries its direction as
  authored data, so the direction a play call takes and the direction a
  track stores have to be the same type. The direction enum's option ids are
  contract ids — identical in every project — so the SDK ships that
  option-id wrapper once instead of every project generating an equivalent
  type beside it:

  ```csharp
  // before
  clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Backward);

  // after
  clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Reverse);
  track.Direction = NeoPlayDirection.Reverse;   // the same type
  ```

  The play methods' `direction` parameters now default to `null` and
  coalesce to `Forward`, because a class instance is not a compile-time
  constant; `PlayOnce()` and `PlayOnce(NeoPlayDirection.Reverse)` are
  unaffected. `NeoPlayDirectionIds` is retired — the wrapper carries its own
  ids, alongside `IsKnown` and `FromOptionId`.

  Two behaviors worth knowing: `FromOptionId` interns an unknown id rather
  than throwing, because strictness belongs in load validation; and `Text`
  without a `NeoClient` returns the raw text id, because the SDK cannot
  reach a project's localization singleton the way generated code can.

- **`NeoSpriteMaskInteractionIds` is replaced by a shipped
  `NeoSpriteMaskInteraction`.** This enum gets the same treatment for the
  same reason — it is the other enum the SDK's own API speaks, through
  `INeoSpriteObjectValue.MaskInteraction` and the renderer — so the two are
  one pattern rather than two. The Unity mapping moves to
  `NeoSpriteMaskInteractions.ToUnity`, which takes an option id directly:

  ```csharp
  // before
  renderer.maskInteraction =
      NeoSpriteMaskInteractionIds.Parse(spriteObject.MaskInteraction);

  // after
  renderer.maskInteraction =
      NeoSpriteMaskInteractions.ToUnity(spriteObject.MaskInteraction);
  ```

  It sits beside the type rather than on it because the wrapper's body has
  to stay byte-identical to what codegen would emit. `MaskInteraction` on
  the value contract stays a `string` option id — that contract is the
  renderer's data view, and generated code bridges to it from its own typed
  member.

  The four smart-tile enums keep being generated per project. They are
  authored data no SDK API mentions, so nothing forces them to be one shared
  type.

- **A child track that runs past its parent clip is truncated rather than
  rejected, and an exhausted window stops writing.** The fit rule that
  failed such a clip at load is gone, and so is the clamp-and-hold that kept
  writing a child's last frame after its window ended. Content that leaned
  on either behavior renders differently. Two validations replace the fit
  rule: `StartFrame` at or past the clip's `Duration`, and an empty or
  inverted crop window.

### Added

- **Animation segments (P48 §1).** A segment is a frame-indexed sequence of
  one member's values — the value-lane counterpart to a clip. Frames are
  sparse and hold until the next authored row or the end of `Duration`, and
  a segment carries no fps of its own: the clip that schedules it owns the
  clock.

- **Segment tracks (P48 §2, §4).** A segment track writes one member of one
  child, one value per applied frame, where a child track hands off to
  another clip. The member it writes is the one its class names with
  `@settings(target:)`, resolved through the class chain, so a project's own
  subclass inherits the target rather than restating it. Both track kinds
  append into one per-frame action stream in `Tracks` order, which is what
  makes last-write-wins the execution order.

  A track resolves its `Segment` **every applied frame** — as a stored
  value, a lookup dereference, or a getter — so a change made mid-playback
  shows on the next frame. Equipping a different asset mid-animation
  re-resolves; a Session-stored `Duration` written at runtime changes the
  window on the next frame.

- **Scheduled playback on every track (P48 §2.1).** Tracks carry
  `Direction` and a crop window (`OffsetStartIndex`, `OffsetEndIndex`), so
  embedding a child clip reversed or cropped is authored composition rather
  than something a call site has to know. Crop applies in the content's own
  frame space before fps scaling, and `Reverse` maps `t → (D−1)−t` across
  the whole resolved timeline, so nested content follows.

- `NeoSchemaClass.targetMemberId` on the wire DTO, mirroring the schema's
  class-level write target.

- `NeoAnimationDefinition` now implements `IDisposable`.

### Fixed

- A sprite written by a clip or a segment now re-renders. `SpriteRenderer`'s
  sprite was assigned only at spawn, so sprite-family writes (`Sprite`,
  `FlipX`, `FlipY`, `MaskInteraction`) updated the data model and never the
  rendered object. `SortingOrder` remains spawn-only — its rendered value
  composes with spawn-time layout state.

## [0.13.1] - 2026-07-29

### Fixed

- Editor device authorization now keeps polling through connection failures,
  HTTP 429 responses, and transient 5xx responses until the device-code
  deadline. `Retry-After` is honored, while permanent OAuth errors still end
  the attempt immediately.
- Editor sign-in now verifies that a freshly constructed token store can read
  the persisted credential before reporting success, and logs the final flow
  outcome without logging access tokens or device codes.
- Linux Secret Service storage now uses a resilient native-plus-file backend:
  failed native operations fall back safely, reads probe both stores, fallback
  credentials migrate back when Secret Service recovers, and sign-out clears
  both stores. The fallback warning is emitted once per editor session.
- Restricted file credentials now require an absolute path outside the Unity
  project, use atomic writes, enforce checked directory/file permissions, and
  verify the stored value after writing.
- The Hello World sample now tracks its Unity package manifest and lockfile,
  and its Spanish localization fixture once again covers the generated Planet
  text used by the downstream test suite.

## [0.13.0] - 2026-07-28

### Breaking

- **Project export schema version 14 → 15.** The runtime accepts only 15, so
  a `project.json` exported before this release fails to load with a message
  telling you to re-export. Re-export the project and regenerate
  `NeoGeneratedTypes.cs` after upgrading. The bump carries two new payloads
  the runtime must have in order to construct instances correctly:
  member/row `init` bodies and the `constructors` collection.

  The compiled-IR gate moves with it: `compilerRevision` 2 → 3. IR compiled
  at revision 3 does not run on 0.11.0, and this release still runs
  revisions 1 and 2.

### Added

- **NeoScript initializers (P43 §1).** A member's stored default — and a
  stored value row — may now carry an `init` instead of a literal `value`:
  authored NeoScript source plus server-compiled IR. `init` is a *member
  initializer* in the ordinary sense, so the SDK **evaluates it every time an
  instance is constructed** rather than reading a value baked at the last
  push. A runtime-constructed instance therefore reflects live state, which
  is the whole point of storing the initializer rather than its output.

  Two consequences worth knowing:

  - An `init` on an **optional** member now materializes at construction.
    `CreateWritableClassValue` previously filled defaults only for required
    members; an initializer is an explicit statement that the member has a
    value, so it is no longer subject to that filter.
  - A value container carries either `value` or `init`, never both. The JSON
    readers reject a container carrying both rather than silently preferring
    one.

- **Declared class constructors (P43 §6, §8).** A class may declare
  constructors with named, non-stored parameters and a NeoScript body.
  Codegen emits one C# constructor per overload, backed by
  `NeoGeneratedTypesSupport.EvaluateDeclaredConstructor` and the new public
  `NeoDeclaredConstructorArgument`:

  ```csharp
  public EyePart(bool IsRight)
      : this(
          client,
          NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
              client,
              "class-eye-part",
              "ctor-eye-part",
              new global::NeoCompose.Runtime.NeoDeclaredConstructorArgument[]
              {
                  new global::NeoCompose.Runtime.NeoDeclaredConstructorArgument(
                      "IsRight", IsRight),
              }),
          false,
          NeoValueOwnership.Session)
  {
  }
  ```

  Construction runs the four steps in order, matching mainstream OO
  languages: member initializers, the base constructor chain, this
  constructor's body against `this`, then the call-site initializer block —
  which wins. An overridden member's initializer still **runs** and is then
  overwritten, which is observably different from never running it when the
  initializer throws.

  Classes that declare no constructor keep their member-wise factory
  constructor unchanged.

- **Construction depth cap.** Nested construction is bounded at 64 — the same
  limit as the NSFunction call depth — with a diagnostic naming the class
  chain. It is counted separately from the NSFunction call stack because
  construction recurses through member initializers and base constructors
  rather than through calls.

- **Constructor record validation at load.** A class's `constructorIds` and a
  constructor's own `classId` are checked in both directions, so neither a
  dangling id nor a disowned record can load. Bodies are checked for the
  `__this__, __root__, __arg_N__` envelope and a void return, and overloads
  that would generate two identical C# constructors are rejected by name.

## [0.12.0] - 2026-07-28

### Changed

- The "legacy pre-0.7 placement" throw in animation child resolution now fires
  only when **not one** `Children` row on the node carries `sourceValueId`
  provenance. Until now a single unstamped row was taken as proof the whole
  placement predated provenance, and any clip reference that did not match on
  that node threw instead of taking 0.10.0's warn-and-skip path.

  Mixed nodes — some rows stamped, some not — are a normal steady state as of
  P44. Rows written by an explicit assignment in Neo source are authored
  content and deliberately carry no stamp, and the authored-provenance backfill
  deliberately leaves rows it cannot structurally correspond to a class default
  unstamped. On such a node an absent slot is an absent slot, so it now skips
  with the usual single deduped warning rather than failing the clip with a
  migration message that would not be true. A node where **every** row is
  unstamped is still genuinely un-migrated data and still throws, with the
  message reworded to "none of its Children rows carry ...". Ambiguity — one
  node carrying two rows with the same stamp — is unchanged and still throws.

### Added

- Nested class rows authored on the web or pushed by the CLI now carry
  `sourceValueId` provenance pointing at the class-default row they were
  materialized from, so a clip declared on a class finally resolves its
  `ChildOverrides` and `Tracks` when that class is used as a `Children` row, at
  any depth. No runtime change was needed for this: `ResolvePlacedChild`
  already matched stamps scoped to one node's `Children`, `CompileChildTracks`
  already recursed into the resolved row's own graph, and the placement clone
  already forwarded `source.sourceValueId ?? source.id`. The stamps are what
  was missing. Sibling rows materialized from one class default carry identical
  child stamps and animate independently, because resolution never leaves the
  node it was asked about.

## [0.11.0] - 2026-07-28

### Breaking

- Generated Sprite properties change type. `public virtual Sprite Portrait`
  becomes `public virtual NeoSprite Portrait`, the optional form becomes
  `NeoSprite?`, the read-only interface member becomes `NeoReadOnlySprite`,
  and the fields token becomes `NeoField<NeoSprite>` /
  `NeoField<NeoSprite?>`. Regenerate `NeoGeneratedTypes.cs` after upgrading;
  a file generated before P42 does not compile against this version, and this
  version's generated file does not compile against 0.10.0.

  Sprite joins the wrapper family `NeoVector2/3(Int)` and `NeoColor` already
  belong to, and the same source-compat rules apply. `Sprite s =
  obj.Portrait;` still compiles through the implicit
  `NeoReadOnlySprite → UnityEngine.Sprite` operator, but `var s =
  obj.Portrait;` now infers `NeoSprite`, and any overload resolved on the
  property's static type can shift. Constructor parameters are deliberately
  **unchanged** and still take `Sprite` / `Sprite?`, matching Color's
  precedent.

  Note that the "required member has no synchronized asset" throw moved off
  the generated getter and onto `Resolve()` and the implicit conversion. The
  message is byte-identical, so `catch`/assert text still matches, but it now
  fires when you resolve rather than when you read — which is the point:
  `obj.Portrait.SliceIndex` no longer throws for an asset that has not been
  synchronized into Unity.

- **Optional sprites need `?.` before `Resolve()`.** User-defined implicit
  conversions do not lift for reference types, so on an optional member
  `Sprite s = obj.Badge;` compiles and then throws a
  `NullReferenceException` at runtime whenever `Badge` has no value — the
  compiler applies the operator to a null `NeoSprite`. Write
  `obj.Badge?.Resolve()` instead. This hazard is new for sprites: before P42
  `obj.Badge` was already `Sprite?`, and the assignment simply would not
  compile. Required sprites are unaffected — the property is non-nullable.

  Every wrapper is a reference type, so the shape is the same on `NeoColor?`
  and the optional `NeoVector*` properties; it is called out here because
  sprites are the ones whose property type just changed under existing code.
  In a `#nullable enable` file the compiler flags the conversion argument
  (CS8604), but that is a warning, and code compiled without nullable context
  gets no diagnostic at all.

- **`obj.Sprite == someUnitySprite` no longer compiles.** The sprite wrapper
  pair deliberately declares only wrapper/wrapper `==` and `!=`. It has no
  mixed wrapper/native overloads, because `UnityEngine.Sprite` is a reference
  type and such an overload would make the far more common `wrapper == null`
  ambiguous. Compare with `obj.Sprite.Resolve() == someUnitySprite`, or
  compare addressable values with
  `obj.Sprite == new NeoSprite(fileId, sliceIndex)`. `NeoVector*` and
  `NeoColor` keep their mixed overloads — their native types are structs, so
  the ambiguity does not arise.

- The project export schema version moves from 13 to 14. `NeoClient` requires
  an exact match with no negotiation, so the web app and this SDK ship
  together: an export produced before P42 is rejected with a clear message,
  and a P42 export is rejected by 0.10.0.

- **This overturns `specs/color-member.md` §6, decisions 5-6.** That decision
  made every structured-leaf wrapper get-only on purpose, and
  `NeoVectorWrapperRetrofitTests` existed specifically to lock the rule in.
  P42 reverses it deliberately, so those tests were rewritten to the new
  contract rather than deleted — the file still guards the boundary, it now
  guards a different one (write-through where the wrapper is bound, local
  mutation where it is detached, and a runtime throw on a read-only instance).

### Added

- Addressable fields on structured leaf members. A sprite's `FileId` and
  `SliceIndex`, a vector's `x`/`y`/`z`, and a color's `r`/`g`/`b`/`a` are now
  readable, writable, and individually addressable.

  **The casing difference is deliberate, not an oversight.** Sprite's fields
  are PascalCase — `FileId`, `SliceIndex` — while vector components stay `x`,
  `y`, `z` and color channels stay `r`, `g`, `b`, `a`. Vectors and colors keep
  lowercase so they read the same as the Unity types they project to
  (`UnityEngine.Vector3.x`, `UnityEngine.Color.r`); renaming them would break
  shipped API for nothing. Sprite has no Unity counterpart to match and it
  already had a PascalCase surface in NeoScript, so it takes the one spelling
  its own convention implies. There is exactly one name per field on every
  public surface — C#, NeoScript, and `.neo` source — and no lowercase
  sprite-field alias exists.

  The **stored record keys are unchanged and stay lowercase everywhere**:
  `fileId`, `sliceIndex`, `x`, `y`, `z`, `r`, `g`, `b`, `a`. Nothing on the
  wire moved. `SpriteValue` — the serialization DTO reached through
  `NeoReadOnlySprite.Value` — therefore still has lowercase `fileId` and
  `sliceIndex` fields, and `{"$partial": {...}}` override envelopes still
  carry stored keys.

- Everywhere that accepted a `Sprite` by inspecting its type now also accepts
  a `NeoSprite`: tile sprite discovery, NeoScript function arguments and their
  validation, and native function arguments. This matters because the implicit
  conversion that keeps compiled call sites working is a *user-defined*
  operator — invisible to `IsAssignableFrom` and to a `value is Sprite`
  pattern — so a consumer that matched on the type rather than taking one as a
  parameter silently stopped seeing sprites when the generated property type
  moved. It cost the tile renderer its sprites with no error of any kind.
  Passing `obj.Portrait` to an NS or native function works as before.

- `NeoReadOnlySprite` / `NeoSprite`, the wrapper pair Sprite never had.
  `NeoReadOnlySprite` exposes `FileId`, `SliceIndex`, `Value`, and
  `Resolve()`; `NeoSprite` adds settable `FileId` / `SliceIndex` and the
  implicit `UnityEngine.Sprite → NeoSprite` conversion so
  `obj.Portrait = someUnitySprite;` still reads the same. The
  `NeoReadOnlySprite → UnityEngine.Sprite` operator is declared on the
  read-only base, so read-only consumers convert too.

- Write-through setters on every writable wrapper — `NeoVector2`,
  `NeoVector2Int`, `NeoVector3`, `NeoVector3Int`, `NeoColor`, `NeoSprite`.
  Binding decides what a field write means. A **bound** wrapper is the one a
  generated getter hands you: `obj.Position.y = 1f` reads the leaf's current
  value, patches the one field, and writes the whole leaf straight back. A
  **detached** wrapper — the implicit operator, `new NeoVector3(...)`, a
  factory argument — is a value copy, and mutating it stays local until it is
  assigned. Whole-value assignment is still a copy in both cases, so
  `a.Position = b.Position` does not link the two members.

  Writing a field on a wrapper obtained from a read-only generated instance
  throws at runtime rather than failing to compile: a read-only instance still
  hands out a wrapper over a writable node, so the guard cannot live in the
  type system. Color channels reject values outside `[0, 1]` rather than
  clamping them, matching what the JSON converter has always done on the read
  path.

- `r`, `g`, `b`, and `a` on `NeoReadOnlyColor`, which previously exposed only
  `Value`. New surface, not a shadow of an inherited member.

- Field-level animation overrides. A frame can override `Sprite.SliceIndex`
  alone and leave `FileId` as whatever the object currently holds — authored
  as a default, written at runtime by game code, or synced live. One authored
  clip then drives every art variant that shares a slice layout, instead of
  needing one copy of the clip per sheet. The same applies to a single vector
  component or color channel, so a frame can animate `Position.y` while
  something else owns `Position.x`.

  A partial structured leaf travels as an explicit `{"$partial": {...}}`
  envelope and is legal only inside an animation override graph; anywhere else
  it is rejected by name.

- `NeoGeneratedTypesSupport.SpriteValue` overloads taking
  `NeoReadOnlySprite?`, which generated sprite setters now bind to. They read
  the addressable pair off the wrapper directly instead of round-tripping
  through a resolved `UnityEngine.Sprite`, so writing a sprite whose file is
  not synchronized into Unity neither throws nor silently loses its slice
  index. The wrong-sheet template check is preserved and reports identically.

### Fixed

- **An unrecognised `$partial` field could land on the wrong component.** The
  animation composer matched the first key or two of each leaf kind and let
  everything else fall through to the last one, so a field write carrying a
  key the kind does not declare — `z` on a `Vector2`, `q` on a `Color` —
  composed a value nobody authored (`y = 5`, `a = 0`) rather than being
  ignored. Export validation already refuses such a key by name, so this was
  only reachable for a field list that got past it; the composer now drops any
  key the leaf does not carry, matching the web resolver's
  `applyStructuredLeafPartial` rule. The cross-runtime frame fixture gained a
  `partialCompositions` section that pins the composed value, and the
  authorship of an empty `{"$partial": {}}`, on both runtimes.

- **A leaf write notified subscribers twice.** `NeoMemberColorWritable.Set`
  and its siblings for every other leaf kind called `NotifyChanged()` after
  `client.SetWritableValue` — which had already delivered the notification to
  the same node through `OnWritableValueChanged` → `OnValueIdChainChanged`. So
  a handler registered with `OnChanged` ran twice per write. This predates
  P42, but P42 is what makes it visible: `obj.Position.y = 1f` routes through
  the leaf's `Set` while `obj.Position = v` routes through
  `NeoMemberClass.SetSerializedValue`, which deliberately leaves notification
  to the child — so the two spellings of the same write notified a different
  number of times. The redundant call is gone from all fifteen leaf setters;
  the `BindNewValue` path still notifies explicitly, because it publishes the
  row before the node's own resolution chain points at it. If you counted
  notifications from a direct `node.Set(...)` call, the count halves.

- **A `$partial` structured-leaf envelope in a member declaration default is
  rejected instead of silently swallowed.** Decision D10 makes the envelope
  legal only inside an animation override graph, and a declaration default is
  never one — but nothing enforced it here. A stray
  `{"$partial": {"sliceIndex": 1}}` under a Sprite declaration was fed
  straight into `SpriteValue`, came back out with a null `fileId`, and became
  "no value" with no diagnostic anywhere. `MemberConverter` now names the
  member and its kind; `MemberValueBaseConverter` raises the same error for a
  carrier deserialized without member context. The `PartialLeafMemberValueBase`
  carrier — declared so an envelope could "report a precise error" that no
  code ever raised — is gone, since the error is now raised before any carrier
  is built. Whole-value defaults are untouched, and a Dictionary default with
  a `$partial` *string* entry still resolves exactly as before.

- **`NeoSprite.SliceIndex` rejects a negative index** with
  `ArgumentOutOfRangeException` rather than writing it through. NeoScript's
  field-assign path already refused one ("Sprite field 'sliceIndex' must be 0
  or greater") and the animation apply path skips one, so the C# wrapper was
  the only way to store a sprite that no resolver can ever resolve and that
  nothing anywhere reports. The upper bound stays a resolution-time null: how
  many slices a file has is known only to a synchronized asset database.

- **Reading a vector or color leaf with no value no longer claims the member
  is "Required".** `NeoVectorValues.ReadVector*` and `NeoColorValues.ReadColor`
  hardcoded the word regardless of the member, so `obj.Glow.a` on an *optional*
  colour with no row reported "Required Color 'Glow' has no value." — false,
  and pointing at the wrong fix. The message now names the field that was read
  and says nothing about requiredness, matching `NeoSprite`:
  `Cannot read 'a': Color 'Glow' has no value.` A whole-value read
  (`obj.Glow.Value`) has no one field to blame and reports
  `Color 'Glow' has no value.` Required members report exactly the same text —
  one shape per condition. Generated getters for required members are
  unaffected; their own `"Required int 'X' has no value."` is accurate and
  unchanged. This predates P42; assertions matching on the old text need
  updating.

## [0.10.0] - 2026-07-28

### Breaking

- `INeoWorldObjectValue` gains `bool Enabled`. Every generated world object
  implements this interface, so a `NeoGeneratedTypes.cs` generated before P41
  **will not compile** on this version — this is a harder break than P40's,
  which degraded silently. Re-export projects and regenerate their C# types
  after upgrading.
- The project export schema version moves from 12 to 13. `NeoClient` requires
  an exact match, so an export produced before P41 is rejected with a clear
  message rather than loading and drawing objects that should be hidden.

### Added

- Optional object children. `NeoObjectBase` carries an `Enabled` bool
  defaulting to true; when false, the object and its whole subtree are
  deactivated and contribute no collider. The renderer still **builds** the
  subtree rather than skipping or destroying it, so a runtime `Enabled` write
  toggles it straight back on, and a clip playing on or through a disabled
  object keeps running and keeps writing values. Disabling a nested part hides
  its subtree regardless of each child's own value, and re-enabling restores
  exactly what was there.

  This is what an empty equipment slot is made of: author the slot once, hide
  it when nothing is equipped, and never write to `Children` at runtime — so
  the authored graph a clip validates against and the graph the player resolves
  against stay identical.

  `INeoObjectSpawnHooks.OnObjectSpawned` still observes a fully-built, fully
  **active** subtree: the renderer applies visibility to the placed root *and*
  every composition child only after the hook returns, so a
  `GetComponentsInChildren` in a spawn hook sees hidden layers too, without
  passing `includeInactive`.

  The value model is now the single source of truth for visibility, the same
  way it already was for `Position`. Calling `SetActive` directly on a
  renderer-spawned object is reverted the next time that object's own `Enabled`
  changes; write `Enabled` instead. Code that hid renderer-spawned objects by
  hand before P41 — which was the only option, since the renderer never called
  `SetActive` — needs to move to `Enabled`.

  Reconciling is scoped to writes that can carry an `Enabled`, so a placement's
  own `Position`, `Size`, or `Sprite` write — and therefore every frame of a
  clip animating the placement itself — costs no visibility work at all. A
  write that reaches the placement through its `Children` still reconciles, but
  compares one bool per object value rather than round-tripping every
  GameObject: a 400-tile layer-link child is one comparison, not 400.

  One edge stays as it was, and it is worth stating precisely: a composition
  part that renders nothing at all — an empty `Children` list, or a subtree cut
  short by the composition depth limit or a cycle — is still destroyed rather
  than kept, and does **not** count towards its parent's rendered children. The
  consequence to watch is the parent, not the part: an object whose only child
  is such a part falls back to drawing its own root sprite, exactly as if it
  had no composition at all. A part whose children are merely *disabled* is
  unaffected — those children are built and deactivated, so the part counts as
  rendered, survives, and suppresses the parent's sprite fallback.

### Changed

- An animation `ChildOverride` or `ChildTrack` naming a child that no placed
  `Children` row carries is now **skipped** with a single warning logged at
  clip-compile time, instead of throwing. The warning is deduped per
  (clip, reference) on the client — not per placement, and not once more per
  parent for a shared child clip — so fifty placements missing the same
  optional slot log once between them rather than fifty times. A full clip-cache
  invalidation resets the dedup, so a genuine re-compile reports again.
  Skipping is scoped to that one reference: the frame's other
  overrides, its own `Overrides`, and its actions all still apply, and a clip
  with one unresolvable track still plays every other track. A skipped track is
  excluded from the `StartFrame + childLength <= Duration` fit check, since
  there is no child clip to fit.

  Ambiguity (a source child matching more than one placed row) and legacy
  pre-0.7 placements (rows without `sourceValueId` provenance) still throw —
  those are data errors, not absent slots. Export and CLI push validation of
  authored clip graphs is unchanged and still strict.

## [0.9.0] - 2026-07-27

### Breaking

- The renderer reads world object members through generated contract
  interfaces instead of reflecting on property names. A `NeoGeneratedTypes.cs`
  generated before P40 implements none of `INeoWorldObjectValue`,
  `INeoObjectCompositionSource`, `INeoColliderSource`,
  `INeoSortingGroupSource`, or `INeoSpriteObjectValue`, so on this version an
  object renders no composition children, no authored collider, and no
  fallback sprite. Re-export projects and regenerate their C# types after
  upgrading.

  There is deliberately no compatibility fallback: `ReadOptionalProperty`,
  `ReadEnumerableProperty`, `ReadObjectName`, and the rest of the name-keyed
  reflection over world object members are gone, along with the per-object,
  per-spawn `GetProperties()` scan they each cost.

### Added

- Sorting groups. An object whose authored `SortingGroup` is non-null gets a
  `UnityEngine.Rendering.SortingGroup` on its root, taking the sorting layer
  and order from the object layer exactly as a `SpriteRenderer` does, so the
  object and its children sort against the world as one unit. `SortAtRoot`
  maps to `SortingGroup.sortAtRoot` and is read once at spawn.
- Sprite renderer state. Authored `FlipX`, `FlipY`, and `MaskInteraction` are
  applied to the `SpriteRenderer`, and `SortingOrder` is **added** to the draw
  order derived from the object's layer group rather than replacing it.
- `NeoSpriteMaskInteractionIds`, pinning the three `NeoSpriteMaskInteraction`
  option ids authored on the web side, with `Parse` onto
  `UnityEngine.SpriteMaskInteraction`.

### Fixed

- An object or tile layer's authored `SortingOrder` was never honoured.
  `NeoGeneratedTileLayerValue` and `NeoGeneratedObjectLayerValue` reflected a
  property named `Order`, which generated layers do not have — they expose
  `SortingOrder` — so the value was always null and the renderer silently fell
  back to its 1000-per-layer stride. Changing a layer's sorting order now
  moves its content.

### Changed

- A tile rendered through a tile layer link names its GameObject after the
  sprite. It previously preferred a reflected `Name` property on the tile
  value; tiles are not world objects and carry no runtime contract, so there
  is no typed equivalent.

## [0.8.0] - 2026-07-26

### Breaking

- Every Neo-authored system record id now carries a reserved `system_` prefix,
  so the sixteen `NeoSmartTileOptionIds` constants changed. The UUID inside is
  unchanged — the re-identification is a pure prefix transform, so a
  pre-migration id is the new one with `system_` removed.

  This package version must be adopted together with the server-side
  re-identification: a build running 0.7.0 against a re-identified project
  compares smart tile option ids that no longer match, and every rule
  evaluates as if its condition were unset. Re-export projects and regenerate
  their C# types after upgrading.

## [0.7.0] - 2026-07-23

### Breaking

- Advanced the required Unity export contract to schema 12. Schema-11 exports
  are rejected; re-export projects and regenerate their C# types before
  upgrading.

### Added

- Added the `FunctionRef` wire kind, recursive Partial Class metadata, and
  NeoScript UI-body metadata used by Neo object animation clips.
- Sparse Partial Class values now materialize only authored fields, preventing
  recursive clip defaults from creating cyclic change-notification graphs.
- Added the typed `NeoAnimationClip<T>` playback API, deterministic frame
  state machine, client-owned scaled-time runner, and per-target playback
  coordination.
- Object placements now resolve through their stable placement row, clone
  mutable authored subtrees per placement, and preserve `sourceValueId`
  provenance for exact child overrides and embedded child tracks.

## [0.6.3] - 2026-07-23

### Changed

- Updated the Hello World NeoScript and runtime fixtures to contract 3.4's
  compact system protection kinds while retaining world authoring metadata
  required by the Unity runtime.

## [0.6.2] - 2026-07-23

### Changed

- Removed the redundant `contentHashAlgorithm` field from game-save record
  descriptors; content hashes continue to use the canonical SHA-256 contract.

## [0.6.1] - 2026-07-22

### Added

- Abstract read-only members now model getter-only C# contracts, and concrete
  read-only overrides can fulfill compatible abstract Immutable/getter-only
  declarations without a NeoScript getter or per-instance value edge.

### Changed

- Schema validation rejects unimplemented abstract read-only members,
  non-read-only instance-backed overrides, setter-required abstract/interface
  contracts, and defaults on abstract read-only declarations.

## [0.6.0] - 2026-07-22

### Breaking

- Advanced the required Unity export contract to schema 11. Schema-10 exports
  are rejected; re-export projects and regenerate their C# types before
  upgrading.

### Added

- Declaration-backed read-only Immutable Class members now resolve one shared
  primitive or composite default through typed runtime and NeoScript getters,
  while omitting per-instance, constructor, clone, save, and session value
  edges.
- Added explicit instance-surface, stored-instance, and read-only Class schema
  projections plus schema-11 validation for invalid declarations and malformed
  instance data.
- Added NeoScript compiler-revision 2 support for declaration-pinned member
  pointers while retaining legacy revision-1 compatibility and rejecting
  unsupported future IR.

## [0.5.2] - 2026-07-22

### Added

- Generated partial classes can request durable, reload-safe editor artifact
  work during `OnDidSynchronize`, with registered handlers, cancellation,
  deterministic dispatch, and synchronization diagnostics.
- `NeoTileGridAuthoringBinding` now exposes an awaitable, cancellable preview
  refresh result and completion event.

### Changed

- Unity synchronization now waits for generated-artifact handlers and matching
  TileGrid authoring previews before publishing final post-sync success.
- Tile and object layer links now resolve their target layer from class-level
  internal-record relations, including inherited targets, instead of the
  removed per-value `layerClassId` sidecar.
- Generated `NeoTileLayerLink` and `NeoObjectLayerLink` system bases are
  abstract; project-authored concrete link classes provide the target relation.

## [0.5.1] - 2026-07-19

### Added

- Generated tile-layer types can provide custom Unity render targets while Neo
  continues to own initial and incremental tile painting.
- Added per-layer callbacks for target creation, initial rendering, live
  changes, and exactly-once teardown, including target identities and destroy
  reasons.

### Changed

- The Hello World collision layer now attaches its `TilemapCollider2D` through
  its generated layer hook instead of a grid-wide lifecycle display-name check.

## [0.5.0] - 2026-07-19

### Breaking

- Advanced the required Unity export contract to schema 10. Schema-9 exports
  are rejected; re-export projects and regenerate their C# types and
  synchronized values.

### Added

- Member access modifiers. Member and interface-member DTOs require an
  `accessModifierKind` (`public` / `protected` / `private`) and fail fast on a
  missing, non-string, or unknown literal. Generic slot substitution keeps the
  slot's own declared modifier. Generated C# emits the declared keyword and
  omits non-public members from read-only and user interfaces; their
  `NeoField` descriptors are `internal`.
- Adopted the `schemaClassInfo` wire name for constructor/clone class info
  (was `classTypeInfo`); the legacy-field guard rejects the removed key with a
  clear migration error instead of silently deserializing null.

## [0.4.0] - 2026-07-17

### Breaking

- Replaced materialized cloud-save payloads with payload-free metadata plus
  paginated record-head manifests, revision deltas, and selected record states.
  Upgrade Neo Compose server and Unity packages together.

### Changed

- Added bounded sparse, chunked, and staged save writes with durable snapshot
  transition polling/retry support.
- Live save synchronization now emits dirty-field patches so concurrent changes
  to untouched fields survive.
- Updated `NeoSmartTileCondition` constants to the stable UUID option identities
  emitted by Neo Compose format-4 projects.

## [0.3.0] - 2026-07-17

### Breaking

- Advanced the required Unity export contract to schema 9. Schema-8 exports are
  rejected; re-export projects and regenerate their C# types and synchronized
  Unity assets.
- Replaced value-backed tile-grid world metadata with class-backed
  `internalRecordRelations`, covering grid imports and layers, compatible and
  default layers, layer-link targets, and smart-tile neighbors. Generated layer
  APIs now bind authored classes, and tile-grid mutation contexts expose class
  IDs plus optional asset-value overrides; code using the former wrapper and
  value-ID APIs must migrate.

### Added

- Added typed `NeoClassRef<T>` definitions and class-default tile and object
  placement APIs.

## [0.2.1] - 2026-07-16

### Added

- Added incremental Unity Editor synchronization using cached export manifests
  and bounded snapshot batches.

### Changed

- Reduced runtime save I/O by suppressing semantic no-op writes and loading full
  save snapshots only when needed.
- Added save partition-home metadata required by copy-on-write snapshot storage.

### Fixed

- Fixed incremental value deltas mutating a detached JSON object instead of the
  cached project document.

## [0.2.0] - 2026-07-15

### Breaking

- Introduced the Class/Member runtime and wire vocabulary and advanced the
  required Unity export contract to schema 8.
- Renamed the schema model base and authoring marker to `NeoSchemaClass`.
- Removed schema-7 compatibility; projects and local saves must be refreshed.

## [0.1.0]

### Added

- Initial scaffold.
- Added schema-v6 `NSFunction` DTOs, generated-call runtime wrappers, typed
  NeoScript action returns, and exactly-once nested/deferred continuation
  dispatch across native and NeoScript Functions.
