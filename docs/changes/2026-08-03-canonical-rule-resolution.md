# Canonical Rule Resolution Across Package Versions

- Date: 2026-08-03
- Status: Implemented and verified

## Decision

Published Rule Definitions are immutable global identities keyed by Rule ID
and definition version. Authority action execution resolves an enabled world
binding directly against that canonical published identity. It does not require
the definition's original package version to match the currently activated
world package version.

## Reason

The content publisher already reuses an identical Rule ID/version across later
package releases. Requiring an exact package-version join during execution made
valid bindings visible in projections but impossible to invoke.

## Verification

- Build and server unit tests
- Existing binding removal still fails closed
- Equip action resolves `EquipItemOnInteractionIntent@1` after a package upgrade
