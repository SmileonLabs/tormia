# Single-pass planar character collision

## Summary

- **Date:** 2026-07-31
- **Area:** local player collision presentation
- **Status:** implemented and verified
- **Issue:** the avatar could pop upward on low-poly ground, object edges, and
  shoreline seams even when no positive vertical movement was requested.

## Decision

The support probe remains observation-only. Horizontal input stays world
planar and is combined with authored gravity, jump velocity, swimming
displacement, or approved controller impulse in one
`CharacterController.Move` call. Project code no longer projects horizontal
input onto a sampled support triangle normal before asking CharacterController
to resolve that same collision.

Role-aware explicit stepping remains available because built-in step solving
cannot distinguish ontology collision roles. A face on the collider currently
supporting the avatar is continuous terrain and cannot become its own step.
Only a distinct `WalkableSupport` collider can provide an explicit step top.

## Ontology boundary

No Triple, Rule Block, Physical Meaning, or durable world data changed.
`LocalCharacterController` still grants the presentation lease. Support
normals, collision flags, and diagnostic traces remain ephemeral observations.
Removing the Physical Meaning still removes local movement.

## Evidence

- `WalkableSlopeDoesNotInjectVerticalDisplacement`
- `CurrentSupportColliderCannotBecomeItsOwnStepObstacle`
- `GroundAdhesionOnWalkableSlopeDoesNotCreatePlanarDrift`
- `ActorBodyCannotBeUsedAsACharacterStep`

The hybrid character motion PlayMode fixture passed 9/9 tests. Development
diagnostics emit `[PlayerMotionTrace]` only for an unrequested rise or an
external Transform write, without creating a Fact or network message.
