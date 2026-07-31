# Equipment input and multi-slot ownership

Date: 2026-07-29

## Decision

TOV keeps the proven wearable proximity flow and reserves `F` for carried
weapons. Wearables use select/approach, ephemeral `interaction_intent`, and
`AutoEquipNearbyWearable` when the actor reaches the target. Weapons use the
`equip_weapon` World Authority action.

Canonical durable ownership is:

```text
item --equipped_by--> actor
item --has_slot-----> canonical slot
```

The wearable inference requires the wearable Triples and
`AutoEquipNearbyWearable`. `equip_weapon` requires the weapon/carryable
Triples and `AutoCarryNearbyCarryable`. A second item in the same authored
slot is rejected, while Waist and RightHand may coexist. The immutable
`equip_wearable` version 1 definition remains in package `1.8.0` history, but
normal Unity input no longer routes `F` to it.

`unequip_equipment` removes only the selected item's `equipped_by` relation and
also retracts the legacy actor-owned `equipped_item` row for that target when
present.

## Presentation boundary

Unity routes `SelectThenEquip` to the local ontology observation and rule
engine, and routes `SelectThenCarry` to the Authority weapon action. It does
not decide permission. Attachment follows `equipped_by`. Weapon animation
selects only equipped entities in the data-owned combat presentation catalog,
so non-weapon equipment does not affect armed locomotion.

Left-click selects and approaches a wearable, publishes the temporary
`interaction_intent`, and clicking the attached wearable executes the existing
ontology unequip action. `F` cannot select, attach, or remove the tube.

## Executable evidence

- Server: `EquipWeapon_CoexistsWithEquipmentInAnotherSlot`,
  `EquipWeapon_WhenSameSlotIsOccupiedIsRejected`,
  `EquipWearable_WithRuleBlockCreatesItemOwnedRelation`, and
  `EquipWearable_WithoutRuleBlockIsRejected`.
- Unity EditMode: `OntologyEquipmentSlotConditionTests`,
  `CombatFDoesNotRouteWearablesAwayFromProximityRule`,
  `PublishedDevelopmentActionsContainEquipAndGuardedDeath`,
  `RightHandWeaponUsesGenericDataDefinedAttachmentContract`, and
  `WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract`.
- Unity PlayMode: `OntologyAttachmentAdapterTests` and scene composition smoke.

Removing the matching Rule Block removes the corresponding behavior, and
Unity has no hidden name-based permission fallback.
