# Interruptible Equipment-to-Locomotion Transition

## Summary

- **Date:** 2026-07-29
- **Owner:** Unity ontology animation presentation
- **Status:** Implemented
- **Observed issue:** Moving immediately after weapon equipment moved the
  avatar while the equip pose remained active. Stopping once and moving again
  allowed `WeaponWalk` to play.

## Cause

`WeaponEquip` and `WeaponUnequip` were authored as non-interruptible, and the
animation adapter ignored locomotion observations while any transient action
presentation was active. Equipment state and movement were valid; presentation
scheduling prevented the transition.

## Decision

The animation manifest remains the owner of interruption policy. An active
transient presentation yields to the current locomotion intent only when its
definition is interruptible and movement is observed. Equipment transitions
are authored as interruptible. Attacks and reactions remain non-interruptible
unless their own manifest entries explicitly change.

This change creates no gameplay Fact and adds no weapon, prefab, mesh, or
object-name exception.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Interruptible equip + movement | Direct transition to catalog-selected `WeaponWalk` | `EquipmentTransitionMetadataAllowsImmediateLocomotion` |
| Non-interruptible transient + movement | Transient presentation retains its authored lock | `EquipmentTransitionMetadataAllowsImmediateLocomotion` |
| Equipment state | Authority-owned `equipped_by` remains unchanged | Existing Authority equipment tests |
