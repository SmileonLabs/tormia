# Slope Grounding and Local Pose Ownership

> The support-tangent part of this record is superseded by
> `2026-07-31-single-pass-planar-character-collision.md`. Support normals remain
> observations and no longer manufacture vertical locomotion.

## Summary

- **Date:** 2026-07-31
- **Area:** player collision presentation and Authority projection ownership
- **Status:** implemented and verified
- **Issue:** the player could pop before landing, drift on contact, or return
  toward an older stored position.

## Intent

Keep local collision resolution stable without granting Unity new gameplay
permission. The complete authored locomotion contract still decides whether
movement is allowed. This change only prevents collision observation and
durable projection refreshes from becoming competing Transform writers.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [x] Ephemeral observation
- [x] Inferred/runtime state
- [x] Unity presentation
- [x] Transport / Authority / infrastructure boundary

## Decision and boundaries

Support-probe proximity does not authorize step climbing. Explicit step rise
requires an actual grounded CharacterController contact, and a walkable slope
normal is treated as continuous terrain. Horizontal locomotion follows the
support tangent, while gravity and ground adhesion remain world-vertical.
Measured CharacterController behavior showed that support-normal adhesion
preserves its planar component and creates backward slope drift.

The local avatar retains presentation ownership of its visible runtime pose
while in session, including short semantic-rebinding windows and the
Rule-Block-removed path. Removing Physical Meaning still disables the movement
coordinator. It does not let an older durable projection snap the avatar back;
entry, recovery, and respawn remain the only explicit durable-pose restore
boundaries.

## Ontology representation

No new Triple, Rule Block, or Physical Meaning was added. The existing
`LocalCharacterController` Physical Meaning continues to enable movement.
`WalkableSupport` remains the collision role for terrain. Support normals,
grounded contact, and collision flags are ephemeral Unity observations.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Grounded slope adhesion produces no planar drift and a continuous slope produces no step rise. | `GroundAdhesionOnWalkableSlopeDoesNotCreatePlanarDrift`, `WalkableSlopeIsNotMisclassifiedAsExplicitStep` |
| Removed | Removing Physical Meaning disables movement without handing the local visible pose to durable projection. | `LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| Edge | Support observed before landing cannot inject upward step displacement; ActorBody remains an obstacle. | `SupportProximityBeforeLandingCannotCreateStepRise`, `ActorBodyCannotBeUsedAsACharacterStep` |

Unity verification completed with 7/7 hybrid-motion PlayMode tests,
14/14 semantic-synchronization PlayMode tests, and 26/26 player
input/ownership EditMode tests passing. The Unity Console contained no errors
after verification.

## Performance and multiplayer impact

The fix uses the support normal already produced by the bounded non-allocating
probe. It adds no polling, durable event, network message, or database write.
The Authority still validates movement permission and receives only ephemeral
collision-resolved pose samples.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] core harness scenario manifest
