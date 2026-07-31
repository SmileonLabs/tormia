# World slope and airborne presentation contract

## Summary

- **Date:** 2026-07-31
- **Status:** Implemented and verified
- **Scope:** Unity presentation and scene-authored collision meaning

## Intent

Keep a local player in grounded locomotion on authored walkable slopes and keep
the airborne lifecycle visible until collision-backed landing actually begins.

## Ownership and boundaries

- Static scene colliders do not acquire gameplay meaning from their shape,
  name, or lack of a Rigidbody. The Tormia world scene now explicitly assigns
  `WalkableSupport` to solid environment colliders and `WaterVolume` to water
  colliders through `OntologyCollisionRoleAdapter` and the project-owned
  Physical Profile Database.
- `OntologyAnimationStateResolver` remains the owner of the ephemeral
  `JumpStart`, `Airborne`/`Fall`, and `Landing` presentation phases for the local
  player. A projected ambient `animation_intent` Fact cannot replace those
  physical phases with Idle before contact-backed landing completes.
- Grounded Idle, locomotion, and equipment states still permit projected
  semantic animation intents. The change does not turn Unity input into a
  gameplay-rule owner and creates no durable per-frame facts.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Authored scene collision meaning | Every environment collider explicitly resolves to `WalkableSupport` or `WaterVolume` | `OntologyWorldCollisionSemanticAssetTests.WorldEnvironmentCollidersHaveExplicitOntologyRoles` |
| Airborne phase precedence | JumpStart, Airborne/Fall, and Landing cannot be replaced by an ambient projected Fact | `OntologyAnimationTransitionTests.PhysicalJumpPhaseOwnsBasePresentationOverProjectedFact` |
| Grounded semantic intent | Idle, locomotion, and equipment states retain projected semantic intent support | `OntologyAnimationTransitionTests.GroundedBaseStateDoesNotSuppressProjectedSemanticIntent` |
| Slope and jump regression | Walkable slopes do not inject a step/pop and an approved jump lands once | `OntologyHybridCharacterMotionTests` targeted PlayMode cases |

## Removal and failure path

Removing a scene collision-role adapter removes the corresponding authored
support meaning and fails the scene asset test. Disabling MotionStateResolver
ownership removes airborne precedence instead of leaving a hidden intent-name
fallback.

