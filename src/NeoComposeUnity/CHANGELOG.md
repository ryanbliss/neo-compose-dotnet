# Changelog

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
