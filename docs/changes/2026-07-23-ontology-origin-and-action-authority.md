# Ontology Origin Ownership and Authority Action Metadata

## Summary

- **Date:** 2026-07-23
- **Owner:** Codex with project owner
- **Status:** Implemented and verified
- **Related request:** Full hardcoding and ontology-policy remediation

## Intent

Removing ontology data must remove its behavior. Runtime observations and
account-owned character data must not become durable world state. Shared-world
actions must use the package metadata selected by World Authority.

## Data classification

- Account profile
- Durable authored world data
- Runtime observation
- Inferred state
- Transport / authority

## Decisions and boundaries

`OntologyWorldState` records origin contributions for each fact. Removing one
origin does not delete an identical contribution owned by another origin.
Local snapshots include only durable-origin facts. Empty executable catalogs
stay empty and unmatched verbs are rejected.

Development package publication is configured as data in
`WorldAuthoritySettings`. The server world projection exposes enabled action
package/version/definition metadata, and Unity uses that metadata for
`execute_action`.

Canonical Authority semantic IDs are ASCII English identifiers. Localized
labels remain aliases and presentation data.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Explicit action definition | Candidate/effect executes | Unity core action tests |
| Empty/removed action catalog | No hidden defaults or arbitrary Fact | `EmptyActionCatalogDoesNotRestoreHiddenDefaults` |
| Overlapping fact origins | Removing profile/appearance leaves durable fact | world-state and projector tests |
| Snapshot | Observation/profile/appearance/inference omitted | save/restore tests |
| Authority action | Enabled projection metadata selects package/version | Authority build and Unity tests |

Final verification completed on 2026-07-23:

- Unity EditMode: 66 passed, 0 failed
- Unity PlayMode: 37 passed, 0 failed
- `scripts/verify-development.ps1 -RequireServices`: passed
- Unity Console after compilation and tests: 0 errors
- Local World Authority container recreated from the verified image; health: `healthy`

## Performance and multiplayer impact

Fact origin sets add a small per-fact memory cost. They prevent destructive
cross-owner removal and avoid persisting ephemeral data. World projections add
one compact enabled-action metadata list and no per-frame database writes.

## Updated documents

- `PROJECT_CONTEXT.md`
- `PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
