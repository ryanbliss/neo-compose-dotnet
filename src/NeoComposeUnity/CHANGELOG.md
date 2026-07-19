# Changelog

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
