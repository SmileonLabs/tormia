# Direction-invariant melee contact

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented; automated and live Unity verification passed
- **Issue:** Targeted attacks could miss from some world directions.

## Runtime evidence

Authority preview and swing approval succeeded, but the failing route ended at
`attack_contact_window_completed_without_contact`. Live inspection found an
11.4-degree facing offset, while the contact adapter queried only the current
weapon Box pose.

## Decision

- Rotate toward the selected projected target before publishing the targeted
  attack intent.
- Continuously sample translation and rotation between the previous and current
  authored weapon contact volumes.
- Keep all samples ephemeral and restricted to the Authority-approved target
  and manifest contact window.

No direction, weapon, prefab, animation or monster name is special-cased.
World Authority continues to own durable damage and combat outcomes.

## Verification

- `CombatFacing_RequiresTargetDirectionBeforeAttackPublication`
- `WeaponContactSweepSubstepsCoverLinearAndAngularMotion`
- `WeaponContactSweepObservesTargetSkippedBetweenFrames`
- Relevant Unity EditMode suites: 72/72 passed
- Live route: `approved_weapon_contact_observed` followed by
  `damage_authority_accepted` from the previously failing direction
