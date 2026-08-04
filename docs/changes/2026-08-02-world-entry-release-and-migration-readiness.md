# World entry release and migration readiness

## Summary

- **Date:** 2026-08-02
- **Owner:** TOV development harness
- **Status:** Implemented and verified

## Decision

World entry verifies an already published immutable content release before any
avatar or world preparation. It does not publish Rule or Action definitions and
does not activate a content package. The development harness fingerprints the
configured Rule and Action source identities so payload drift is found before
play rather than during entry.

Database process health is not schema readiness. With `-RequireServices`, the
harness verifies every ordered repository migration against
`platform_schema_migrations`.

An empty ledger on a legacy persistent volume is never assumed to mean that all
migrations are pending. A separate, explicit adoption tool verifies schema,
indexes, constraints, and migration-016 ownership backfill before it may record
001 through 016. It refuses partial ledgers and incompatible fingerprints.
Migration 017 remains a normal data repair and is never adopted from schema
shape.

## Verification

- `scripts/verify-development.ps1 -SkipServerBuild`: passed
- `scripts/verify-development.ps1 -SkipServerBuild -RequireServices`: passed
- Current local database: all 17 migrations recorded
- Legacy fingerprint dry run: schema and ownership backfill compatible; no data
  written

The initial apparent empty-ledger result was traced to shell quoting in the
readiness verifier. Direct `psql` invocation using the configured database and
user corrected the false result. No live database mutation was performed.
