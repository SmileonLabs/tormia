# Player ontology production contract

## Summary

- Date: 2026-07-29
- Area: player world semantics, transient locomotion, Authority evaluation,
  Unity presentation, development harness
- Status: implemented and verified

## Intent

Make the player follow the same reusable ontology production discipline as
weapons and monsters before the live hunting loop is accepted. Player movement
must not exist merely because a Unity controller or Authority endpoint can move
an avatar.

## Data classification

- Account profile: appearance, template, and account preferences only
- Durable world data: player role, capability, life, faction, action, movement
  tuning, Physical Meaning, animation intents, and Rule Block bindings
- Runtime observation: input samples, grounding, position, and motion status
- Unity presentation: local collision, approved prediction, reconciliation,
  animation, and feedback

## Decision and boundary

Player semantic contract version 2 introduces the `move_avatar` immutable
action and assigned `MovePlayerFromIntent` Rule Block. Every runtime intent
identifies that action. World Authority evaluates the action and assigned rule
before accepting the ephemeral sample, and its scheduler independently
requires the same semantic contract.

Unity may present local movement after Authority approval, but it does not own
the permission. Account entry migrates only newly introduced contract stages;
the runtime foundation no longer restores every default player Fact, so a
versioned user removal remains removed.

## Ontology expression

- Triples: `has_concept -> Actor`, `has_concept -> PlayerControlled`,
  `grants_capability -> Locomotion`, `locomotion_action -> move_avatar`,
  numeric `movement_speed`, `is_alive -> true`,
  `physical_profile -> AuthorityKinematic`, idle/move animation intents
- Rule Block: `MovePlayerFromIntent/?actor`
- Action: `move_avatar@1`
- Package: `social_village@3.3.0`
- Physical Meaning: `AuthorityKinematic`

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Complete contract | Movement action and rule accept an ephemeral locomotion request with no durable mutation | `PlayerLocomotionRuleAcceptsCompleteEphemeralContract`; `AvatarTriplesDeclareCompleteGroundLocomotionMeaning` |
| Rule or action removed | Authority evaluation and Unity predicted locomotion route disappear | `RemovingPlayerLocomotionRuleBlockRemovesMovementBehavior`; `RemovingLocomotionActionFactRemovesUnityIntentRoute` |
| Re-entry after removal | Versioned runtime entry does not reauthor removed semantics | `RuntimeFoundationDoesNotReauthorRemovedPlayerSemantics` |

## Performance and multiplayer impact

Movement samples remain transient and do not advance world revision. Each
accepted sample currently uses the standard side-effect-free action preview
path. The scheduler refreshes eligible avatars once per second and refuses
ambiguous or incomplete contracts. A later optimization may cache immutable
evaluation inputs without changing this semantic boundary.

## Updated documents

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- `scripts/verify-development.ps1`
