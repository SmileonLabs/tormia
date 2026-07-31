# Package-scoped actions and isolated Authority smoke

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development harness
- **Status:** Implemented and verified
- **Related issue:** World entry stopped with
  `action_definition_version_conflict` after publishing a new package version.

## Intent

Align the database identity of an Authority action with the exact identity
already used by execution and projection. Prevent regression tests from leaving
accounts, worlds, packages, or definitions in the shared local database.

## Data classification and boundaries

- Durable content: immutable package-scoped Authority action definitions.
- Durable world data: the package ID/version enabled by a revisioned world
  command.
- Test data: disposable accounts, worlds, entities, facts, and packages inside
  the isolated smoke database only.
- Unity presentation: localized account-flow status; raw technical details stay
  in the Console.

## Decision

`action_effect` uniqueness is
`packageId + packageVersion + actionId + definitionVersion`. Non-action content
keeps global ID/version uniqueness because current rule bindings do not include
package identity.

The normal combat smoke creates and destroys its own Compose network and
volumes. Persistent local services require explicit `-UseExistingServices`.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Cross-package reuse | Identical action ID/version may be published into two package identities. | Isolated Authority combat smoke |
| Same tuple changed | Publishing different content under the same complete tuple still conflicts. | Repository checksum check |
| Enabled | Package 1.4.0 publishes and the selected character enters the world. | Live Unity/Authority entry verification |
| Removed | Disabled package rejects execution and the isolated smoke leaves no Docker volume. | Isolated Authority combat smoke |
| Presentation | Raw rejection code stays in Console while UI uses a localized message. | Unity EditMode localization-key test |

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `infrastructure/README.md`
- `tests/harness/core-regression-scenarios.json`
