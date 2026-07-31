# Drowning recovery motion ownership

## Summary

- **Date:** 2026-07-31
- **Status:** Implemented and verified
- **Request:** Remove the apparent automatic jump and stalled movement when the player enters water.

## Intent

Keep ontology-derived drowning recovery progressing even while its own
presentation temporarily disables `CharacterController`.

## Data classification

- Runtime observation
- Inferred state
- Unity presentation

## Decision and boundaries

`movement_mode Drowning` remains an inferred ontology result. Unity presents
that result, but it does not infer drowning from a mesh or object name.
Drowning recovery is evaluated before ordinary player input and controller
locomotion. While it owns movement, ephemeral velocity and input presentation
are cleared. Removing the relation releases the presentation immediately.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Recovery continues after disabling the controller and restores the confirmed anchor. | `OntologyInferenceSchedulingTests.PlayerInputAdvancesRecoveryAfterControllerIsDisabled` |
| Disabled | Removing `Drowning` restores the controller and releases recovery ownership. | `OntologyInferenceSchedulingTests.RetractedDrowningImmediatelyReleasesMovementPresentation` |
| Regression | Existing checkpoint recovery and hybrid collision motion remain valid. | `OntologyInferenceSchedulingTests`; `OntologyHybridCharacterMotionTests` |

## Performance and multiplayer impact

The change adds no durable per-frame event. Recovery remains a local
presentation of Authority-derived state; only the existing confirmed recovery
checkpoint is persisted.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
