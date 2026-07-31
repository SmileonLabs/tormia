# Player motion session convergence

## Summary

- **Date:** 2026-07-28
- **State:** Implemented and verified
- **Scope:** Authority runtime activation, transient movement intent, checkpoint
  synchronization, Zone presence, Unity reconciliation

## Intent

Prevent proximity actions from comparing a visible Unity player against an
unrelated, indefinitely refreshed Redis position.

## Data ownership

- PostgreSQL owns durable avatar checkpoints and the authored
  `movement_speed` maximum.
- Redis owns only expiring session input and runtime motion.
- Unity captures requested locomotion and presents a reconciled transform. It
  does not own final distance validation.

## Decision and boundaries

World entry calls an authenticated runtime activation endpoint. The endpoint
clears the prior session input and seeds runtime motion from the current durable
avatar transform. Accepted checkpoint commands repeat that synchronization
after commit.

The movement intent now includes requested walk/run speed. Authority clamps it
to `movement_speed`; removing or invalidating that Fact disables movement.
Only avatars backed by a live Zone session are advanced, so durable
registrations cannot keep Redis motion alive while offline.

Unity horizontal reconciliation is enabled in the project-owned world scene.
Terrain, gravity, swimming, and collision authority remain separate adapters;
this change does not pretend the current kinematic slice owns them.
The input adapter is the sole `CharacterController.Move` owner. Reconciliation
returns a bounded horizontal delta that is combined with locomotion and gravity
in that single call, preventing alternating ground/collision resolution.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Walk/run | Requested speed is preserved below the authored cap | `RequestedLocomotionSpeedIsClampedByAuthoredMaximum` |
| Removed | Missing/zero speed produces no server movement | `InvalidOrRemovedMovementSpeedDisablesMovement` |
| New session | Sequence 1 is accepted after prior-session input is cleared | `ClearingIntentAllowsANewSessionSequence` |
| Unity convergence | Project world enables Authority horizontal reconciliation | `WorldPlayerReconcilesWithActivatedAuthorityMotion` |
| Ground stability | Correction ignores Y and is bounded before the single movement call | `AuthorityCorrectionIsHorizontalAndBounded` |
| Server regression | All Authority unit tests pass | 20/20 |
| Unity regression | Targeted combat EditMode tests pass | 25/25 |

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
