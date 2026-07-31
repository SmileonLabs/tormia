# Rule-Block-gated weapon equipment

- Date: 2026-07-28
- Status: Implemented
- Scope: World Authority, Unity ontology data, physical presentation, harness

## Decision

Weapon equipment uses the same ontology production contract as the inflatable
ring. A weapon is equipable only when its canonical Triples, assigned
`AutoCarryNearbyCarryable` Rule Block, `RightHandCarry` attachment profile, and
`HandheldWeapon` physical profile are all present.

The immutable `equip_weapon` action requires the active Rule Block. World
Authority projects rule bindings into the command-scoped ontology snapshot
instead of relying on a Unity controller or object-name exception.

## Migration

- Development package `1.7.0` publishes `AutoCarryNearbyCarryable`.
- `equip_weapon` definition version `5` requires
  `has_rule_block -> AutoCarryNearbyCarryable`.
- The three starter swords author `physical_profile -> HandheldWeapon` and
  receive the default Rule Block binding.
- Legacy editable worlds receive missing catalog facts and bindings through
  revisioned commands before the one-time semantic-contract marker is written.

## Evidence

- Unity EditMode:
  `WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract`
- Server unit:
  `EquipWeapon_ReplacesForwardRelationAndMaintainsInverseRelation`
- Server disabled case:
  `EquipWeapon_WithoutRuleBlockIsRejected`
- Harness:
  `weapon-ontology-vertical-slice`

## Follow-up: immutable Rule Block version

The current tube/equipment Rule Blocks had already been published as version 1
by the local catalog before their present conditions were authored. Their
current definitions are therefore version 2, and both development publication
and new world bindings resolve those same catalog versions. Reusing version 1
for changed content is rejected instead of overwriting durable world meaning.
