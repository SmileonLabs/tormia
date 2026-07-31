# Contact-delivered melee impact

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development
- **Status:** Implemented; automated Unity verification passed, manual combat check pending
- **Request:** Make damage and impact feedback occur when the sword reaches the monster.

## Intent

Replace target-raycast-timed impact with an authored animation window and an
actual weapon-volume overlap against the Authority-approved target.

## Data classification

- Durable authored world data: `attack_contact_mode`
- Runtime observation: weapon/approved-target overlap
- Unity presentation: animation time, hit point and VFX
- Authority transport: preview followed by revisioned action execution

## Decision and boundary

World Authority remains the only owner of damage, death, loot, range and
cooldown. Unity owns only animation playback and ephemeral contact observation.
There is no elapsed-time, raycast-point or asset-name damage fallback.

## Ontology expression

- Triple: `tool attack_contact_mode WeaponContactWindow`
- Rule Block: `MeleeAttackOnPrimaryIntent`
- Physical/presentation data: authored weapon `BoxCollider`
- Animation data: normalized contact window on `Anim_Sword_LightAttack`
- Navigation presentation: stop distance derived from the authored weapon and
  target collider geometry, bounded by the Authority-authored attack range

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Enabled | Approved-target overlap inside the manifest window requests Authority damage | `MeleeDamageRequiresAuthoredContactModeAndAnimationWindow`, `DisabledWeaponColliderStillObservesOnlyApprovedContact` |
| Removed | Missing Rule Block, contact Triple, window or overlap causes no damage/VFX | asset contract tests plus existing removed-Rule-Block Authority tests |
| Regression | Manifest synchronizes window metadata into the runtime database | `CurrentAnimationAssetsMatchManifestProjection` |

The three relevant EditMode suites passed 68/68 tests on 2026-07-29.

## Performance and multiplayer

The overlap query runs only during one non-looping contact window and writes no
per-frame database event. Authority re-evaluates the durable action after
contact, so multiplayer state remains server-owned.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
