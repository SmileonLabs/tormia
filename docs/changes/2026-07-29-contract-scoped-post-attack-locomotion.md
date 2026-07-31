# Contract-scoped Post-attack Locomotion

## Summary

- **Date:** 2026-07-29
- **Owner:** Authority player motion and Unity animation presentation
- **Status:** Implemented
- **Observed issue:** Moving after an accepted attack alternated between
  stopping and resuming, while the avatar could move with the attack pose
  still active.

## Intent

Preserve smooth locomotion after combat without allowing Unity to invent
movement or attack permission.

## Data classification

- Durable monster health and life changes remain world data.
- Player input and locomotion approval remain ephemeral runtime state.
- Animation interruption remains Unity presentation metadata authored in the
  validated animation manifest.

## Decision and boundaries

Projection revision is no longer used as a proxy for a changed locomotion
contract. Unity fingerprints only the selected world/Zone, the avatar's own
Facts, its Rule Block bindings, and its unique immutable locomotion action.
Unrelated monster damage preserves an accepted locomotion lease; a changed,
ambiguous, incomplete, or removed player contract invalidates it.

`Anim_Sword_LightAttack` is explicitly interruptible in the source manifest
and generated runtime database. Approved movement can therefore select the
catalog-owned `WeaponWalk` presentation instead of moving under a retained
attack pose. This metadata does not change Authority action evaluation or
damage.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Monster damage revision | Existing locomotion approval is preserved | `UnrelatedMonsterRevisionPreservesApprovedLocomotionContract` |
| Player contract changed or removed | Prediction is invalidated until Authority accepts again | `ChangedOrRemovedPlayerLocomotionContractRequiresReapproval` |
| Attack followed by movement | Manifest-authored attack yields to locomotion | `SwordLightAttackYieldsPresentationToApprovedLocomotion` |
| Non-interruptible transient | Presentation lock remains active | `EquipmentTransitionMetadataAllowsImmediateLocomotion` |

## Performance and multiplayer impact

Fingerprint work runs only when a durable projection arrives, not every frame.
It excludes unrelated entity Facts, avoids additional network requests, and
keeps Authority as the shared multiplayer gameplay owner.

## Updated documents

- `PROJECT_CONTEXT.md`
- `PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
