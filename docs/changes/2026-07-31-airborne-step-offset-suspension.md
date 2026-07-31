# Airborne Step Offset Suspension

> Superseded by
> `2026-07-31-single-owner-collision-resolved-player-motion.md`. The built-in
> step offset is now always zero and explicit stepping accepts only authored
> `WalkableSupport` collision roles.

## Decision

`CharacterController.stepOffset` is available only for supported ground
locomotion. The Unity movement adapter suspends it during takeoff, falling, and
unsupported descent, then restores the scene-authored value after support is
confirmed.

## Reason

Jump animation occurrence was already isolated from vertical motion, but a
descending capsule could still interpret a floor seam or object edge as a
climbable step. CharacterController could therefore move the transform upward
without any positive authored vertical velocity.

## Contract

- Authority and Rule Blocks continue to own jump permission and parameters.
- Unity owns the ephemeral collision observation and step presentation.
- Landing may remove downward velocity but cannot create upward velocity.
- Disabling or removing jump does not disable ordinary supported stair
  traversal.

## Evidence

- `FallingFromRaisedSupportCannotUseStepClimbToMoveUp`
- `ApprovedParabolicJumpUsesOneControllerMoveAndLands`
