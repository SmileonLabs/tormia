# Combat pointer surface disambiguation

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented
- **Problem:** Clicking the visible lower area of a monster could narrowly miss
  its trigger and hit terrain at the same world footprint. The shared click was
  then routed to movement, so attack appeared delayed until another click
  happened to hit the trigger.

## Decision

- Continue scanning exact ray hits until a projected combat-target presenter is
  found instead of accepting the first projected non-combat entity.
- If no exact combat hit exists, allow a terrain hit directly beneath an active,
  projected combat target to recover that target using its authored interaction
  collider footprint, small serialized padding, and an unobstructed camera line.
- Keep this as ephemeral Unity observation only. World Authority still owns
  hostility, life, range, cooldown, Rule Block assignment, animation approval,
  contact, and damage.

## Enabled and removed paths

- With the authored attack and swing Rule Blocks, a click on the target or its
  visible ground footprint can enter the existing Authority preview route.
- Removing either Rule Block, required Facts, or the projected combat presenter
  creates no Unity attack fallback; the click remains available to movement.

## Evidence

- `CombatPointerAssist_UsesOnlyAuthoredColliderFootprint`
- `CombatPointerAssist_RecoversGroundHitBeneathProjectedTarget`
- Existing `RemovingPrimaryAttackRuleBlockRemovesAttackBehavior`
- Existing `PrimaryAttackRejectsFriendlyOrDefeatedTarget`

## Verification

- Unity EditMode: 240/240 passed
- Unity PlayMode: 53/53 passed
- `scripts/verify-development.ps1 -RequireServices -RequireUnityMcp`: passed
