# Runtime Lifecycle and World-Entry Convergence

## Summary

- **Date:** 2026-07-30
- **Owner:** TOV development harness
- **Status:** Structural implementation and live Unity verification complete
- **Related request:** Structural regression recovery for Rule Block removal,
  floating entry, dead autonomous actors, combat targeting, and sliding
  locomotion presentation.

## Intent

Make lifecycle, live position, entry grounding, and locomotion presentation
converge from their declared owners instead of relying on cached Unity state or
the player's first input.

## Data classification

- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundaries

World Authority owns active lifecycle eligibility and autonomous scheduling.
Runtime registries own ephemeral live position. Durable transforms remain
checkpoints and static placement data, not substitutes for missing live motion.
Unity owns grounding collision and animation presentation. During entry, the
avatar root remains active while its renderers and input stay gated. Durable
checkpoint restoration, collision grounding, ephemeral input/animation reset,
and checkpoint confirmation run in one ordered transaction. The runtime gate
opens only after that transaction succeeds.

The generic runtime gate cannot claim a GameObject inside the local avatar
presentation boundary. Avatar-attached toast presentation is gated as a
behaviour instead of disabling the avatar root. Entry also completes one
transient zero-motion Authority handshake for the assigned locomotion action
and Rule Block before input and renderers are exposed.

Only `execute_action` command results participate in command-based animation
resolution. Other accepted Authority commands bypass that queue.

Meaning-package removal remains ownership-scoped: package contributions are
retracted atomically while unrelated authored, lifecycle, tuning, or
differently owned facts survive.

## Ontology expression

- `is_alive=true` is required for autonomous actors and their targets.
- Target, chase, and attack still require their assigned immutable actions,
  Rule Blocks, semantic profiles, and `AuthorityKinematic` meaning.
- No monster, prefab, mesh, or instance name is consulted.
- Idle and locomotion animation intents remain ephemeral presentation results.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | A living, complete actor uses fresh runtime target position and remains scheduled. | `ArbitraryEntityWithCompleteContractCanAttackPlayerFaction`, `LiveRuntimeOwnerDoesNotFallBackToDurableSpawn` |
| Disabled | Death or contract removal evicts autonomous motion; package Rule Block removal retracts its owned triples/meaning. | `ActorLosingOntologyEligibilityEvictsItsRuntimeMotion`, `run-meaning-package-authority-smoke.ps1` |
| Regression | Entry restores durable data first, keeps the avatar root active, prepares collision and clears stale input/animation while hidden, validates locomotion with Authority, and opens input only after `InWorld`. Non-action commands do not queue animation work. | `RuntimeGateNeverDisablesLocalAvatarPresentationBoundary`, `WorldEntryPresentationPreparesBeforeSessionActivation`, `PendingGroundingRetriesWhenControllerBecomesReady`, `SwordLightAttackYieldsPresentationToApprovedLocomotion`, `AcceptedAnimationIntentWaitsForPresentationReadinessButReplayDoesNot` |

## Performance and multiplayer impact

The existing cached configuration refresh performs one set difference and
removes only stale ephemeral actor records. No per-frame durable write was
added. Entry preparation is local presentation state only. A grounded entry
correction uses one revisioned checkpoint command only when the restored pose
changes.

## Live verification

Verified through the connected Unity 6000.4.7f1 Editor:

- Unity recompile completed with zero Console errors.
- Two targeted EditMode runtime-gate tests and four targeted PlayMode
  entry/composition tests passed.
- A stored account and selected world completed the production entry flow with
  session `InWorld`, entry phase `Active`, player input and animation enabled,
  locomotion Authority approval true, `CharacterController.isGrounded=true`,
  vertical support bias `-1`, and animation intent `Idle`.
- Unity Console contained zero errors and warnings after entry.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Core regression scenarios
