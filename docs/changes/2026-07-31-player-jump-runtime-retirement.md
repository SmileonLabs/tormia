# Player Jump Runtime Retirement

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV development harness
- **Status:** Implemented
- **Request:** Remove the current player jump code before redesigning the capability.

## Intent

Remove the production player-jump route as one complete contract. Ground
locomotion, authored gravity, falling, landing observation, combat, equipment,
and respawn remain available.

## Data classification

- [ ] Account profile
- [x] Durable authored world data template
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decisions and boundaries

- Unity no longer creates a Space-key jump input, queues a jump edge, applies a
  jump impulse, or requests/presents Authority jump approval.
- Runtime player-intent transport carries ground locomotion only.
- The development content package no longer publishes `jump_avatar` or
  `JumpPlayerFromIntent`, and PlayerProfile no longer authors `jump_action`,
  `jump_height`, or the `Jump` capability.
- Authored `gravity_acceleration` remains because it owns support-surface
  settling and falling; it does not create a jump.
- Generic animation catalog content may retain unused jump clips for future
  authoring, but the player runtime has no route that selects `JumpStart`.
- Existing durable worlds are not destructively rewritten by this change.
  Legacy jump facts may remain visible until a separately approved data
  migration removes them, but no Unity or server runtime path consumes them.

## Ontology representation

The active player movement contract is now:

`locomotion_action -> MovePlayerFromIntent -> AuthorityKinematic -> Unity ground movement`

The retired route is:

`jump_action -> JumpPlayerFromIntent -> jump impulse / JumpStart`

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Ground locomotion | `move_avatar` and `MovePlayerFromIntent` remain published and executable. | `DevelopmentPackagePublishesGroundLocomotionAndRetiresJump` |
| Jump removed | PlayerProfile, development package, and RuleDatabase contain no active player jump production contract. | `AvatarTriplesDeclareCompleteGroundLocomotionMeaning`; `DevelopmentPackagePublishesGroundLocomotionAndRetiresJump` |
| Gravity/fall regression | Gravity continues settling the CharacterController and airborne/landing remain observations. | `AuthoredGravityContinuesSettlingWithoutNewMovementIntent`; `LosingGroundUsesAirborneObservationWithoutGameplayAction` |

## Performance and multiplayer impact

One transient boolean and its secondary Authority preview were removed from the
player-intent path. No per-frame durable write was added. Multiplayer ground
locomotion continues through the existing ephemeral Authority lease.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Language pack CSV / migration
- [x] Harness scenario list
