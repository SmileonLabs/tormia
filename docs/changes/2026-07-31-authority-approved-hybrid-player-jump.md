# Authority-approved hybrid player jump

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV gameplay/ontology
- **Status:** Implemented and verified
- **Request:** Restore player jump with CharacterController locomotion and isolated Rigidbody reactions without returning to layered local patches.

## Intent

Provide a reusable player jump production contract in which authored data and
an assigned Rule Block grant permission, World Authority approves a transient
grounded request, and Unity presents the approved parabolic motion through one
collision owner.

## Data classification

- Durable world data: jump action ID, takeoff/gravity/ground-stick tuning,
  impact response meaning, assigned Rule Block.
- Runtime observation: grounded state, vertical velocity, impulse velocity.
- Unity presentation: CharacterController collision, animation and temporary
  Rigidbody reaction.
- Authority transport: evaluation-only action request and result.

## Decision and boundary

`JumpPlayerFromIntent` owns jump permission. Unity does not jump merely because
Space was pressed. Normal motion has exactly one CharacterController move per
frame. A temporary Rigidbody reaction can own the transform only while the
controller is suspended. Grounded and velocity values are ephemeral and do not
create durable events.
The new optional grounded constraint is canonicalized away when `false`, so
legacy immutable action versions retain their checksum; `true` remains part of
the jump action's immutable content.
The previous jump Rule and action version 1 remain immutable. The hybrid
contract publishes both as version 2 in package 3.8.1, and player semantic
contract version 6 migrates the active binding instead of overwriting history.

## Ontology representation

- Relations: `jump_action`, `jump_takeoff_speed`,
  `gravity_acceleration`, `ground_stick_velocity`,
  `impact_response_profile`
- Capability: `Jump`
- Action: `jump_avatar`
- Rule Block: `JumpPlayerFromIntent`
- Physical Meaning: `AuthorityKinematic` with `ControllerImpulse` or
  `TemporaryRigidbodyReaction`

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Complete authored contract plus positive grounded observation receives Authority approval and starts the authored parabolic motion. | Unity `OntologyPlayerProductionContractTests`; server `GroundedRuntimeConstraintRejectsMissingOrNegativeObservation` |
| Disabled | Removing `jump_action` or the Rule Block, or omitting grounded observation, removes/rejects jump with no local fallback. | Unity removed-path test and server runtime-constraint test |
| Regression | Gravity, landing and controller impulses share one move; temporary Rigidbody ownership is exclusive; legacy actions do not conflict on the new default field. | CollisionFlags and impulse-decay EditMode tests; hybrid PlayMode tests; action canonicalization server tests; Unity compile/console verification |
| Existing world | An avatar bound to jump Rule version 1 retracts that binding, receives version 2, and enters the world successfully. | Live Authority publication returned HTTP 200; active DB binding is version 2 and version 1 is retracted; runtime entry reached `IsWorldRuntimeReady` |

## Performance and multiplayer impact

No per-frame world Fact or database event is added. Jump requests are
evaluation-only. The vertical collision arc remains local presentation until
Authority has a terrain collision representation; this change does not claim
server-simulated remote vertical motion.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `AGENTS.md`
- `tests/harness/core-regression-scenarios.json`
