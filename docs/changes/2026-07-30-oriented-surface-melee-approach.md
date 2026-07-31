# Oriented-surface melee approach

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented
- **Problem:** Authority-approved swings could intermittently finish without
  contact because navigation counted a vertical weapon Box diagonal as
  horizontal reach. Once contact was corrected, the navigation inset could
  still reduce the stop radius below the combined actor/target body surfaces
  and visually overlap both characters.

## Decision

- Derive click-approach distance from the currently oriented authored weapon
  contact surface and the approved target collider's closest point.
- Prime the previous contact pose on every frame before the manifest contact
  window without reporting impact.
- Treat the player controller and target interaction collider as authored
  Physical Meaning surfaces. Their planar clearance is a hard lower bound that
  the attack-range navigation inset cannot reduce.
- Keep contact observation ephemeral. World Authority remains the sole owner
  of damage, cooldown, health, death, and loot.

## Enabled and removed paths

- With the contact-mode Triple, assigned attack Rule Block, validated manifest
  window, and actual approved-target overlap, Unity may submit the existing
  Authority damage action.
- Removing any required semantic stage or failing to overlap the approved
  target produces no damage and no timing, raycast, name, or distance fallback.

## Evidence

- `ContactApproachUsesOrientedWeaponSurface`
- `PrimedContactSampleSeedsFirstApprovedSweep`
- `CombatBodyClearanceUsesAuthoredColliderSurfaces`
- `CombatApproach_NeverConsumesPhysicalBodyClearanceAsRangeInset`
- Existing contact-delivery and Rule-Block-removed regression suites

## Verification

- Unity EditMode: 238/238 passed
- Unity PlayMode: 53/53 passed
- `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`: passed
