# Player grounding and idle Authority reconciliation

## Summary

- **Date:** 2026-07-29
- **Owner:** Codex with project owner
- **State:** Superseded by the transactional entry-preparation contract
- **Scope:** Unity world-entry grounding and local player motion presentation

## Intent

Remove the first-step downward snap after world entry and the repeated
start/stop feeling caused by a low-frequency Authority position pulling
against responsive local movement.

## Data classification

- Unity presentation
- Ephemeral runtime transport state

No account profile, durable world Fact, inference result, Rule Block, or
physical profile is added or changed.

## Decision and boundaries

Checkpoint restoration continues to own the durable avatar Transform. The
former inactive-avatar/`OnEnable` grounding path has been removed. The current
contract keeps the avatar root active but hidden and input-gated, then performs
collision grounding inside the explicit world-entry presentation transaction.

The input adapter remains the only owner of `CharacterController.Move`.
Authority reconciliation contributes no correction while local movement input
is active or while Authority still reports `moving`. After input stops,
Authority reports `idle`, and the configured settle delay expires, the client
applies a bounded horizontal correction through the same controller move.
Unity does not widen combat/equipment distance and does not publish the
presentation correction as a durable command.

## Ontology expression

This is presentation timing for the existing Authority motion contract. It
introduces no canonical ontology term and no hidden gameplay permission.
Authored `movement_speed` remains the server-owned maximum.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Active | Entry preparation grounds before the `InWorld` transition opens input | `WorldEntryPresentationPreparesBeforeSessionActivation` |
| Active | Idle Authority correction is horizontal and bounded | `AuthorityCorrectionIsHorizontalAndBounded` |
| Disabled | Active local input or Authority `moving` produces no correction | `AuthorityCorrectionWaitsForLocalStopAndIdleAuthority` |
| Regression | First movement remains on the support surface | `EntryGroundingKeepsFirstMoveOnSupportSurface` |

Full Unity verification passed: EditMode 174/174 and PlayMode 47/47.

## Performance and multiplayer impact

No new polling, database write, durable event, or per-frame allocation path is
introduced. The existing Authority motion poll remains unchanged. Correction
is suppressed during input and resumes only from a stable idle sample.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
