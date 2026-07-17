# Changelog

## [0.3.0] - 2026-07-17

### Breaking

- Requires `com.ryanbliss.neocompose` 0.3.0 and schema-9 project exports with
  class-backed tile-grid relations. Re-export projects and regenerate their C#
  types and synchronized Unity assets before upgrading.

## [0.2.1] - 2026-07-16

### Changed

- Updated the realtime package for payload-light save listing, on-demand
  snapshot loading, and save partition-home metadata.
- Requires `com.ryanbliss.neocompose` 0.2.1.

## [0.2.0] - 2026-07-15

### Breaking

- Updated the realtime synchronization contract for the Neo Compose Class/Member
  runtime vocabulary and Unity export schema 8.
- Requires `com.ryanbliss.neocompose` 0.2.0; schema-7 runtime exports are no
  longer accepted.

## [0.1.0]

### Added

- Initial Convex realtime synchronization package.
