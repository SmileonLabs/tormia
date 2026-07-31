# Locomotion Approval and Grounding Continuity

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV team
- **Status:** Verified
- **Related issue or request:** The avatar appeared to be lifted by a nearby monster.

## Intent

Prevent unrelated combat projection changes from suspending player grounding
or manufacturing an airborne/jump presentation.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

World Authority still owns whether new locomotion and jump intents are
accepted. Unity may continuously present authored gravity for an already
visible capsule, automatically revalidate an invalidated movement lease with a
zero-motion transient sample, and select airborne presentation from observed
ground contact. None of these operations creates a gameplay permission or
durable Fact.

## Ontology expression

- The locomotion fingerprint includes only locomotion/jump contract inputs.
- Unrelated combat, equipment, profile, respawn, and presentation semantics are
  excluded.
- Relevant contract removal still disables prediction until Authority accepts
  the complete contract again.
- Gravity remains Physical Meaning; jump initiation remains Rule- and
  Authority-owned.
- Automatic lease recovery is armed only after the explicit zero-motion
  world-entry preparation succeeds, so the two transient requests cannot race
  their sequence numbers.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Initial entry preparation runs alone; afterward combat/equipment revisions preserve movement approval, an invalidated contract automatically revalidates, and authored gravity settles the capsule. | `RuntimeRevalidationWaitsForInitialEntryPreparation`; `UnrelatedAvatarCombatStatePreservesApprovedLocomotionContract`; `AuthoredGravityContinuesSettlingWithoutNewMovementIntent` |
| Disabled / removed | Removing or changing a relevant movement Fact, Rule Block, world/Zone scope, or move action stops prediction until Authority reaccepts it. | `ChangedOrRemovedPlayerLocomotionContractRequiresReapproval` |
| Presentation edge | Losing contact without an approved jump presents Airborne/Fall, never JumpStart. | `LosingGroundWithoutApprovedJumpDoesNotPresentJumpStart`; `scripts/verify-development.ps1` passed |

## Performance and multiplayer impact

Revalidation uses an ephemeral zero-motion sample and is rate-limited after a
rejection. It does not advance world revision or write per-frame Facts.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
