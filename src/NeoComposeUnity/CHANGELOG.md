# Changelog

## [Unreleased]

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
