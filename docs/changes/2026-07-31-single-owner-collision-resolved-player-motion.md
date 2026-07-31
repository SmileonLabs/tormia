# Single-owner collision-resolved player motion

## Decision

Player movement now uses `physical_profile LocalCharacterController`.
Physical Meaning declares an exclusive Unity `motionDriver` and
`collisionRole`. `OntologyCharacterMotionCoordinator` owns normal
`CharacterController.Move` calls, while input, gravity, swimming, entry
grounding and approved impact presentation contribute to that path.

The earlier server collisionless X/Z integration is retired for players.
Authority still evaluates the locomotion/jump Action and assigned Rule Block,
validates ownership and Zone bounds, and accepts only sequence-ordered runtime
samples. Unity publishes the collision-resolved pose as ephemeral state for
remote presentation. No pose sample advances world revision or writes a Fact.

## Collision contract

- `WalkableSupport` may provide current support and an explicit step top.
- `ActorBody`, `DynamicProp`, triggers and water volumes cannot become steps.
- Built-in `CharacterController.stepOffset` remains zero.
- Entry grounding uses the real capsule radius and skin width, then initializes
  contact once while input remains gated.
- Removing/changing Physical Meaning disables the matching driver lease and
  adapters; no component-name or prefab fallback remains.

## Migration

- Player avatar semantic contract: version 7.
- Development content package: `social_village` version `3.9.0`.
- `MovePlayerFromIntent`: Rule version 2.
- `JumpPlayerFromIntent`: Rule version 3.
- Existing player avatars replace `AuthorityKinematic` with
  `LocalCharacterController` and migrate both Rule Block bindings on entry.
- Autonomous actors keep `AuthorityKinematic`.

## Evidence

- `ActorBodyCannotBeUsedAsACharacterStep`
- `LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease`
- `EntryGroundingKeepsFirstMoveOnSupportSurface`
- `WorldEntryPresentationPreparesBeforeSessionActivation`
- `ResolvedPoseRegistryRejectsStalePoseSequence`
- `CollisionResolvedPoseMustRemainInsideAuthoredZone`

