# Authority-aligned drowning respawn

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development
- **Status:** Implemented and verified
- **Request:** Prevent the player from sliding back toward the drowning point and use a fixed respawn location.

## Intent

Return a drowning player to one stable, confirmed checkpoint and prevent stale
Authority motion from pulling the recovered Unity presentation back into water.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundaries

The accepted avatar checkpoint remains the durable respawn source. `Drowning`
is inferred state. Unity owns only the sink, teleport, vertical-velocity reset,
and collision-grounding presentation. The one recovery checkpoint command
updates the durable pose and causes Authority to re-seed ephemeral motion.

Water overlap, falling, and the sinking presentation are unsafe checkpoint
samples. They cannot replace the confirmed respawn anchor. There is no
mesh-name, prefab-name, or fixed-coordinate gameplay exception.
The checkpoint owner is resolved from the actor that owns local player input;
an arbitrary Authority identity discovered first in scene order is invalid.

## Ontology expression

- Observed: `Actor occupies WaterRegion`
- Inferred: `Actor movement_mode Drowning`
- Durable recovery command: `save_avatar_checkpoint`
- Unity presentation: sink, restore, ground, suspend/resume reconciliation

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | `Drowning` restores to the confirmed checkpoint and re-seeds Authority motion. | `DrowningRecoveryUsesConfirmedCheckpointAnchor`, `CheckpointReseedSuspendsStaleAuthorityCorrection` |
| Disabled / removed | Missing or removed `Drowning` performs no recovery and releases movement immediately. | `RetractedDrowningImmediatelyReleasesMovementPresentation` |
| Regression | Water, falling, and active recovery cannot overwrite the checkpoint. | `CheckpointCaptureRejectsTransientUnsafePositions` |
| Identity regression | Account entry, remote exclusion, and checkpoint ownership select the local-input avatar rather than a monster. | `LocalAvatarResolutionIgnoresArbitraryWorldIdentity` |

## Performance and multiplayer impact

Recovery sends one revisioned checkpoint command, not per-frame events.
Reconciliation is suspended only until that command completes.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Harness scenario manifest
